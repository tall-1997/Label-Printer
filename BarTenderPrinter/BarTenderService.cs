using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Drawing;

namespace BarTenderPrinter
{
    public class BarTenderService : IBarTenderService
    {
        private dynamic _btApp;
        private string _previewSdkPath;
        private string _previewHostPath;
        private string _previewUnavailableReason = "正在检测 BarTender .NET SDK";
        private string _previewCacheKey = "";
        private bool _connected;
        private bool _offlineMode;
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private readonly BlockingCollection<Action> _staQueue = new BlockingCollection<Action>();
        private readonly Thread _staThread;
        private int _staThreadId;
        private bool _disposed;
        private DateTime _lastOperationTime = DateTime.MinValue;
        private const int MinOperationIntervalMs = 2000;
        private const int MaxPrintAttempts = 3;
        private const int BusyRetryDelayMs = 250;

        public bool IsConnected => _connected;
        public bool IsOfflineMode => _offlineMode;
        public bool IsPreviewAvailable => !string.IsNullOrEmpty(_previewSdkPath) && !string.IsNullOrEmpty(_previewHostPath);
        public string PreviewUnavailableReason => _previewUnavailableReason;

        public BarTenderService()
        {
            TryLoadPreviewSdk();
            _staThread = new Thread(RunStaLoop) { IsBackground = true, Name = "BarTender COM STA" };
            _staThread.SetApartmentState(ApartmentState.STA);
            _staThread.Start();
        }

        private void RunStaLoop()
        {
            _staThreadId = Thread.CurrentThread.ManagedThreadId;
            foreach (var action in _staQueue.GetConsumingEnumerable())
                action();
        }

        private T InvokeSta<T>(Func<T> action)
        {
            if (Thread.CurrentThread.ManagedThreadId == _staThreadId) return action();
            if (_disposed) throw new ObjectDisposedException(nameof(BarTenderService));
            var tcs = new TaskCompletionSource<T>();
            _staQueue.Add(() =>
            {
                try { tcs.SetResult(action()); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            return tcs.Task.GetAwaiter().GetResult();
        }

        private Task<T> InvokeStaAsync<T>(Func<T> action)
        {
            if (Thread.CurrentThread.ManagedThreadId == _staThreadId)
            {
                try { return Task.FromResult(action()); }
                catch (Exception ex) { return Task.FromException<T>(ex); }
            }
            if (_disposed) return Task.FromException<T>(new ObjectDisposedException(nameof(BarTenderService)));
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                _staQueue.Add(() =>
                {
                    try { tcs.SetResult(action()); }
                    catch (Exception ex) { tcs.SetException(ex); }
                });
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
            return tcs.Task;
        }

        private void InvokeSta(Action action)
        {
            InvokeSta<object>(() => { action(); return null; });
        }

        public bool Connect()
        {
            return InvokeSta(ConnectCore);
        }

        private bool ConnectCore()
        {
            LoggerService.Info("正在连接 BarTender...");

            try
            {
                var comType = Type.GetTypeFromProgID("BarTender.Application");
                if (comType == null)
                {
                    LoggerService.Warn("BarTender COM 未注册 (GetTypeFromProgID 返回 null)");
                    _offlineMode = true;
                    _connected = false;
                    return false;
                }
                LoggerService.Info("BarTender COM 已注册");

                _btApp = Activator.CreateInstance(comType);
                if (_btApp == null)
                {
                    LoggerService.Warn("BarTender COM 创建失败");
                    _offlineMode = true;
                    _connected = false;
                    return false;
                }
                LoggerService.Info("BarTender COM 实例创建成功");

                try
                {
                    _btApp.Visible = false;
                    LoggerService.Info("BarTender Visible=False 设置成功");
                }
                catch (Exception ex)
                {
                    LoggerService.Warn($"设置 Visible 失败: {ex.Message}");
                }

                _connected = true;
                _offlineMode = false;
                LoggerService.Info("BarTender 连接成功");
                return true;
            }
            catch (Exception ex)
            {
                LoggerService.Error($"BarTender 连接失败: {ex.Message}");
                _offlineMode = true;
                _connected = false;
                _btApp = null;
                return false;
            }
        }

        public List<string> GetTemplateDataSources(string templatePath)
        {
            return InvokeSta(() => GetTemplateDataSourcesCore(templatePath));
        }

        private List<string> GetTemplateDataSourcesCore(string templatePath)
        {
            var result = new List<string>();
            if (!_connected || _btApp == null) return result;

            _operationLock.Wait();
            try
            {
                EnsureOperationInterval();
                dynamic btFormat = null;
                try
                {
                    btFormat = OpenFormat(templatePath);
                    dynamic subStrings = null;
                    try
                    {
                        subStrings = btFormat.NamedSubStrings;
                        var count = (int)subStrings.Count;
                        for (int i = 1; i <= count; i++)
                        {
                            dynamic sub = null;
                            try
                            {
                                sub = subStrings.Item(i);
                                var name = (string)sub.Name;
                                if (!string.IsNullOrEmpty(name)) result.Add(name);
                            }
                            catch { }
                            finally { ReleaseComObject(sub); }
                        }
                    }
                    finally { ReleaseComObject(subStrings); }
                    CloseFormat(btFormat);
                }
                catch (Exception ex)
                {
                    LoggerService.Error($"获取数据源失败: {ex.Message}");
                    CloseFormat(btFormat);
                }
            }
            finally
            {
                _operationLock.Release();
            }
            return result;
        }

        public void RunDiagnostics(string templatePath)
        {
            InvokeSta(() => RunDiagnosticsCore(templatePath));
        }

        private void RunDiagnosticsCore(string templatePath)
        {
            LoggerService.Info("========== BarTender 诊断开始 ==========");
            
            // 1. 检查连接状态
            LoggerService.Info($"[诊断] 连接状态: {(_connected ? "已连接" : "未连接")}");
            LoggerService.Info($"[诊断] COM 对象: {(_btApp != null ? "已创建" : "未创建")}");
            
            if (!_connected || _btApp == null)
            {
                LoggerService.Error("[诊断] BarTender 未连接，无法进行诊断");
                return;
            }

            // 2. 检查模板文件
            LoggerService.Info($"[诊断] 模板路径: {templatePath}");
            LoggerService.Info($"[诊断] 模板存在: {File.Exists(templatePath)}");
            
            if (!File.Exists(templatePath))
            {
                LoggerService.Error("[诊断] 模板文件不存在");
                return;
            }

            // 3. 尝试打开模板
            dynamic btFormat = null;
            try
            {
                LoggerService.Info("[诊断] 尝试打开模板...");
                btFormat = OpenFormat(templatePath);
                LoggerService.Info("[诊断] 模板打开成功");
                
                // 4. 检查模板属性
                try
                {
                    LoggerService.Info($"[诊断] 模板名称: {btFormat.Name}");
                    LoggerService.Info($"[诊断] 模板文件名: {btFormat.FileName}");
                }
                catch (Exception ex)
                {
                    LoggerService.Warn($"[诊断] 获取模板属性失败: {ex.Message}");
                }

                // 5. 检查数据源
                try
                {
                    dynamic subStrings = null;
                    try
                    {
                        subStrings = btFormat.NamedSubStrings;
                        var count = (int)subStrings.Count;
                        LoggerService.Info($"[诊断] 数据源数量: {count}");
                        for (int i = 1; i <= Math.Min(count, 5); i++)
                        {
                            dynamic sub = null;
                            try
                            {
                                sub = subStrings.Item(i);
                                LoggerService.Info($"[诊断] 数据源 {i}: {sub.Name}");
                            }
                            catch (Exception ex)
                            {
                                LoggerService.Warn($"[诊断] 获取数据源 {i} 失败: {ex.Message}");
                            }
                            finally { ReleaseComObject(sub); }
                        }
                    }
                    finally { ReleaseComObject(subStrings); }
                }
                catch (Exception ex)
                {
                    LoggerService.Warn($"[诊断] 获取数据源列表失败: {ex.Message}");
                }

                // 6. 检查打印机
                try
                {
                    LoggerService.Info($"[诊断] 默认打印机: {btFormat.Printer}");
                }
                catch (Exception ex)
                {
                    LoggerService.Warn($"[诊断] 获取打印机失败: {ex.Message}");
                }

                // 7. 检查 BarTender 版本
                try
                {
                    LoggerService.Info($"[诊断] BarTender 版本: {_btApp.Version}");
                }
                catch (Exception ex)
                {
                    LoggerService.Warn($"[诊断] 获取版本失败: {ex.Message}");
                }

                // 9. 检查许可
                try
                {
                    LoggerService.Info($"[诊断] 许可状态: {_btApp.LicenseStatus}");
                }
                catch (Exception ex)
                {
                    LoggerService.Warn($"[诊断] 获取许可状态失败: {ex.Message}");
                }

                CloseFormat(btFormat);
            }
            catch (Exception ex)
            {
                LoggerService.Error($"[诊断] 打开模板失败: {ex.Message}");
                LoggerService.Error($"[诊断] 异常类型: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    LoggerService.Error($"[诊断] 内部异常: {ex.InnerException.Message}");
                }
                CloseFormat(btFormat);
            }
            
            LoggerService.Info("========== BarTender 诊断结束 ==========");
        }

        public PrintResult Print(string templatePath, Dictionary<string, string> fieldValues, string printer, int copies)
        {
            return PrintAsync(templatePath, fieldValues, printer, copies).GetAwaiter().GetResult();
        }

        public Task<PrintResult> PrintAsync(string templatePath, Dictionary<string, string> fieldValues, string printer, int copies)
        {
            var values = new Dictionary<string, string>(fieldValues ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            return InvokeStaAsync(() => PrintCore(templatePath, values, printer, copies));
        }

        public Task<string> ExportPreviewAsync(string templatePath, Dictionary<string, string> fieldValues)
        {
            var values = new Dictionary<string, string>(fieldValues ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            return InvokeStaAsync(() => ExportPreviewCore(templatePath, values));
        }

        private string ExportPreviewCore(string templatePath, Dictionary<string, string> fieldValues)
        {
            if (!IsPreviewAvailable)
                throw new InvalidOperationException(_previewUnavailableReason);
            if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
                throw new FileNotFoundException("预览模板不存在", templatePath);

            _operationLock.Wait();
            try
            {
                var cacheKey = BuildPreviewCacheKey(templatePath, fieldValues);
                var outputPath = Path.Combine(AppPaths.PreviewDirectory, "current-preview.png");
                if (string.Equals(cacheKey, _previewCacheKey, StringComparison.Ordinal) && IsValidPreviewImage(outputPath))
                    return outputPath;

                var previewPath = ExportPreviewWithHost(templatePath, fieldValues, outputPath);
                _previewCacheKey = cacheKey;
                return previewPath;
            }
            catch (Exception ex)
            {
                var message = GetBaseExceptionMessage(ex);
                LoggerService.Error($"生成标签预览失败: {message}");
                throw new InvalidOperationException(message, ex);
            }
            finally
            {
                _operationLock.Release();
            }
        }

        private string ExportPreviewWithHost(string templatePath, Dictionary<string, string> fieldValues, string outputPath)
        {
            Directory.CreateDirectory(AppPaths.PreviewDirectory);
            var requestId = Guid.NewGuid().ToString("N");
            var requestPath = Path.Combine(AppPaths.PreviewDirectory, $"preview-{requestId}.json");
            var errorPath = Path.Combine(AppPaths.PreviewDirectory, $"preview-{requestId}.error.txt");
            var candidatePath = Path.Combine(AppPaths.PreviewDirectory, $"preview-{requestId}.png");
            try
            {
                var request = new PreviewHostRequest
                {
                    SdkPath = _previewSdkPath,
                    TemplatePath = Path.GetFullPath(templatePath),
                    OutputPath = candidatePath,
                    Fields = fieldValues ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                };
                File.WriteAllText(requestPath, JsonSerializer.Serialize(request), new UTF8Encoding(false));
                var startInfo = new ProcessStartInfo
                {
                    FileName = _previewHostPath,
                    Arguments = $"\"{requestPath}\" \"{errorPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(_previewHostPath)
                };
                using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 BarTender 预览宿主");
                if (!process.WaitForExit(60000))
                {
                    try
                    {
                        process.Kill(true);
                        if (!process.WaitForExit(5000))
                            LoggerService.Warn("BarTender 预览宿主终止后仍未退出");
                    }
                    catch (Exception ex)
                    {
                        LoggerService.Warn($"终止 BarTender 预览宿主失败: {ex.Message}");
                    }
                    throw new TimeoutException("BarTender 预览生成超时（60 秒）");
                }
                if (process.ExitCode != 0)
                {
                    var detail = File.Exists(errorPath) ? File.ReadAllText(errorPath).Trim() : "预览宿主未返回错误详情";
                    throw new InvalidOperationException($"BarTender 预览宿主失败: {detail}");
                }
                if (!IsValidPreviewImage(candidatePath))
                    throw new InvalidDataException("BarTender 预览宿主未生成有效图片");
                File.Move(candidatePath, outputPath, true);
                return outputPath;
            }
            finally
            {
                TryDeletePreviewFile(requestPath);
                TryDeletePreviewFile(errorPath);
                TryDeletePreviewFile(candidatePath);
            }
        }

        private static void TryDeletePreviewFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { LoggerService.Warn($"清理预览临时文件失败: {Path.GetFileName(path)}; {ex.Message}"); }
        }

        private sealed class PreviewHostRequest
        {
            public string SdkPath { get; set; }
            public string TemplatePath { get; set; }
            public string OutputPath { get; set; }
            public Dictionary<string, string> Fields { get; set; }
        }

        internal static Dictionary<string, string> ProjectPreviewFields(
            IDictionary<string, string> fieldValues, IEnumerable<string> availableFields)
        {
            var available = new HashSet<string>(availableFields ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            return (fieldValues ?? new Dictionary<string, string>())
                .Where(item => available.Contains(item.Key))
                .ToDictionary(item => item.Key, item => item.Value ?? "", StringComparer.OrdinalIgnoreCase);
        }

        internal static string BuildPreviewCacheKey(string templatePath, Dictionary<string, string> fieldValues)
        {
            var builder = new StringBuilder(Path.GetFullPath(templatePath));
            builder.Append('\0').Append(File.GetLastWriteTimeUtc(templatePath).Ticks);
            foreach (var item in fieldValues.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
                builder.Append('\0').Append(item.Key).Append('\0').Append(item.Value ?? "");
            return builder.ToString();
        }

        private void TryLoadPreviewSdk()
        {
            try
            {
                var sdkPath = FindPreviewSdkPath();
                if (string.IsNullOrEmpty(sdkPath))
                {
                    _previewUnavailableReason = "未找到 BarTender 2022 .NET SDK";
                    return;
                }
                var hostPath = Path.Combine(AppContext.BaseDirectory, "BarTenderPreviewHost.exe");
                if (!File.Exists(hostPath))
                {
                    _previewUnavailableReason = "缺少 BarTender .NET Framework 预览宿主";
                    return;
                }
                var version = AssemblyName.GetAssemblyName(sdkPath).Version;
                if (version == null || version.Major != 11 || version.Minor != 3)
                {
                    _previewUnavailableReason = $"BarTender SDK 版本不匹配: {version}";
                    return;
                }
                var probeError = Path.Combine(AppPaths.PreviewDirectory, $"preview-probe-{Guid.NewGuid():N}.error.txt");
                Directory.CreateDirectory(AppPaths.PreviewDirectory);
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = hostPath,
                        Arguments = $"--probe \"{sdkPath}\" \"{probeError}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetDirectoryName(hostPath)
                    };
                    using var probe = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动预览环境探针");
                    if (!probe.WaitForExit(30000))
                    {
                        try { probe.Kill(true); probe.WaitForExit(5000); } catch { }
                        throw new TimeoutException("BarTender 预览环境探针超时");
                    }
                    if (probe.ExitCode != 0)
                    {
                        var detail = File.Exists(probeError) ? File.ReadAllText(probeError).Trim() : "探针未返回错误详情";
                        throw new InvalidOperationException(detail);
                    }
                }
                finally
                {
                    TryDeletePreviewFile(probeError);
                }
                _previewSdkPath = sdkPath;
                _previewHostPath = hostPath;
                _previewUnavailableReason = "";
                LoggerService.Info($"BarTender 预览宿主已就绪: SDK {version}; {sdkPath}");
            }
            catch (Exception ex)
            {
                _previewSdkPath = "";
                _previewHostPath = "";
                _previewUnavailableReason = $"BarTender 预览环境检测失败: {GetBaseExceptionMessage(ex)}";
                LoggerService.Warn(_previewUnavailableReason);
            }
        }

        private static string FindPreviewSdkPath()
        {
            const string fileName = "Seagull.BarTender.Print.dll";
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Seagull");
            if (!Directory.Exists(root)) return "";

            try
            {
                return Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories)
                    .Select(path =>
                    {
                        try
                        {
                            return new
                            {
                                Path = path,
                                Version = AssemblyName.GetAssemblyName(path).Version,
                                IsX64 = IsX64Pe(path),
                                IsSdkRedistributable = IsSdkRedistributablePath(path)
                            };
                        }
                        catch (Exception ex)
                        {
                            LoggerService.Debug($"读取 BarTender SDK 元数据失败: {path}; {ex.Message}");
                            return null;
                        }
                    })
                    .Where(candidate => candidate?.IsX64 == true && candidate.Version is { Major: 11, Minor: 3 })
                    .OrderByDescending(candidate => candidate.IsSdkRedistributable)
                    .ThenByDescending(candidate => candidate.Version)
                    .Select(candidate => Path.GetFullPath(candidate.Path))
                    .FirstOrDefault() ?? "";
            }
            catch (Exception ex)
            {
                LoggerService.Debug($"搜索 BarTender SDK 失败: {root}; {ex.Message}");
                return "";
            }
        }

        internal static bool IsSdkRedistributablePath(string path)
        {
            var segments = Path.GetFullPath(path)
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index <= segments.Length - 3; index++)
            {
                if (string.Equals(segments[index], "SDK", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(segments[index + 1], "Redist", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(segments[index + 2], "x64", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        internal static bool IsX64Pe(string path)
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 0x40 || reader.ReadUInt16() != 0x5A4D) return false;
            stream.Position = 0x3C;
            var peHeaderOffset = reader.ReadInt32();
            if (peHeaderOffset < 0 || peHeaderOffset > stream.Length - 6) return false;
            stream.Position = peHeaderOffset;
            return reader.ReadUInt32() == 0x00004550 && reader.ReadUInt16() == 0x8664;
        }

        private static bool IsValidPreviewImage(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length == 0) return false;
            try
            {
                using var image = Image.FromFile(path);
                return image.Width > 0 && image.Height > 0;
            }
            catch { return false; }
        }

        private static string GetBaseExceptionMessage(Exception exception)
        {
            return exception.GetBaseException().Message;
        }

        private PrintResult PrintCore(string templatePath, Dictionary<string, string> fieldValues, string printer, int copies)
        {
            if (!_connected || _btApp == null)
                return new PrintResult(false, "BarTender 未连接", $"template={templatePath};printer={printer};copies={copies};connected={_connected}");

            for (var attempt = 1; attempt <= MaxPrintAttempts; attempt++)
            {
                _operationLock.Wait();
                try
                {
                    return PrintInternal(templatePath, fieldValues, printer, copies);
                }
                catch (Exception ex) when (IsComBusyError(ex) && attempt < MaxPrintAttempts)
                {
                    LoggerService.Warn($"BarTender 忙碌，{BusyRetryDelayMs}ms 后重试 ({attempt}/{MaxPrintAttempts}): {ex.Message}");
                }
                catch (Exception ex)
                {
                    LoggerService.Error($"打印失败: {ex.Message}");
                    return new PrintResult(false, ex.Message, $"type={ex.GetType().Name};template={templatePath};printer={printer};copies={copies};attempt={attempt};message={ex.Message}");
                }
                finally
                {
                    _operationLock.Release();
                }
                Thread.Sleep(BusyRetryDelayMs);
            }
            return new PrintResult(false, "BarTender 持续忙碌，打印作业提交失败", $"template={templatePath};printer={printer};copies={copies};attempts={MaxPrintAttempts}");
        }

        private PrintResult PrintInternal(string templatePath, Dictionary<string, string> fieldValues, string printer, int copies)
        {
            dynamic btFormat = null;
            try
            {
                LoggerService.Info($"打开模板: {templatePath}");
                btFormat = OpenFormat(templatePath);
                LoggerService.Info("模板打开成功");

                var missing = new List<string>();
                foreach (var kv in fieldValues ?? new Dictionary<string, string>())
                {
                    try
                    {
                        btFormat.SetNamedSubStringValue(kv.Key, kv.Value);
                        LoggerService.Info($"数据源: {kv.Key}={kv.Value}");
                    }
                    catch (Exception ex)
                    {
                        if (IsComBusyError(ex)) throw;
                        missing.Add(kv.Key);
                    }
                }
                if (missing.Count > 0)
                {
                    CloseFormat(btFormat);
                    return new PrintResult(false, $"模板中未找到字段: {string.Join(", ", missing)}", $"template={templatePath};printer={printer};copies={copies};missingFields={string.Join("|", missing)}");
                }

                try { btFormat.Printer = printer; LoggerService.Info($"打印机: {printer}"); }
                catch (Exception ex)
                {
                    CloseFormat(btFormat);
                    if (IsComBusyError(ex)) throw;
                    return new PrintResult(false, $"设置打印机失败: {ex.Message}", $"type={ex.GetType().Name};template={templatePath};printer={printer};copies={copies};message={ex.Message}");
                }

                dynamic printSetup = null;
                try
                {
                    printSetup = btFormat.PrintSetup;
                    printSetup.IdenticalCopiesOfLabel = copies;
                    LoggerService.Info($"份数: {copies}");
                }
                catch (Exception ex)
                {
                    CloseFormat(btFormat);
                    if (IsComBusyError(ex)) throw;
                    return new PrintResult(false, $"设置份数失败: {ex.Message}", $"type={ex.GetType().Name};template={templatePath};printer={printer};copies={copies};message={ex.Message}");
                }
                finally { ReleaseComObject(printSetup); }

                object printResult = btFormat.PrintOut(false, false);
                if (printResult is bool boolResult && !boolResult)
                {
                    CloseFormat(btFormat);
                    return new PrintResult(false, "BarTender 打印返回失败", $"template={templatePath};printer={printer};copies={copies};result=false");
                }
                LoggerService.Info("打印作业已提交");

                CloseFormat(btFormat);
                return new PrintResult(true, "");
            }
            catch (Exception ex)
            {
                LoggerService.Error($"打印失败: {ex.Message}");
                CloseFormat(btFormat);
                if (IsComBusyError(ex)) throw;
                return new PrintResult(false, ex.Message, $"type={ex.GetType().Name};template={templatePath};printer={printer};copies={copies};message={ex.Message}");
            }
        }

        private static bool IsComBusyError(Exception ex)
        {
            var message = ex?.Message?.ToLowerInvariant() ?? "";
            return message.Contains("正在打印") ||
                   message.Contains("当前正在") ||
                   message.Contains("busy") ||
                   message.Contains("0x80010105") ||
                   message.Contains("rpc_e_serverfault") ||
                   message.Contains("0x80010001") ||
                   message.Contains("rpc_e_call_rejected");
        }

        private static List<string> GetNamedSubStringNames(dynamic btFormat)
        {
            var result = new List<string>();
            dynamic subStrings = null;
            try
            {
                subStrings = btFormat.NamedSubStrings;
                var count = (int)subStrings.Count;
                for (int i = 1; i <= count; i++)
                {
                    dynamic sub = null;
                    try
                    {
                        sub = subStrings.Item(i);
                        var name = (string)sub.Name;
                        if (!string.IsNullOrWhiteSpace(name) && !result.Contains(name, StringComparer.OrdinalIgnoreCase))
                            result.Add(name);
                    }
                    catch { }
                    finally { ReleaseComObject(sub); }
                }
            }
            catch { }
            finally { ReleaseComObject(subStrings); }
            return result;
        }

        private void EnsureOperationInterval()
        {
            var elapsed = (DateTime.Now - _lastOperationTime).TotalMilliseconds;
            if (elapsed < MinOperationIntervalMs)
            {
                var delay = (int)(MinOperationIntervalMs - elapsed);
                LoggerService.Debug($"操作间隔等待 {delay}ms");
                Thread.Sleep(delay);
            }
            _lastOperationTime = DateTime.Now;
        }

        private dynamic OpenFormat(string templatePath)
        {
            dynamic formats = null;
            try
            {
                formats = _btApp.Formats;
                return formats.Open(templatePath, false, "");
            }
            finally { ReleaseComObject(formats); }
        }

        private static void CloseFormat(dynamic btFormat)
        {
            if (btFormat == null) return;
            try { btFormat.Close(false); } catch { }
            finally { ReleaseComObject(btFormat); }
        }

        private static void ReleaseComObject(object value)
        {
            if (value == null) return;
            try
            {
                if (System.Runtime.InteropServices.Marshal.IsComObject(value))
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(value);
            }
            catch { }
        }

        public string[] GetAvailableTemplates(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return new string[0];
            return Directory.GetFiles(directory, "*.btw", SearchOption.TopDirectoryOnly);
        }

        public string[] GetPrinters()
        {
            try
            {
                var printers = new string[System.Drawing.Printing.PrinterSettings.InstalledPrinters.Count];
                System.Drawing.Printing.PrinterSettings.InstalledPrinters.CopyTo(printers, 0);
                return printers;
            }
            catch { return new string[0]; }
        }

        public void Disconnect()
        {
            InvokeSta(DisconnectCore);
        }

        private void DisconnectCore()
        {
            _operationLock.Wait();
            try
            {
                if (_btApp != null)
                {
                    try { _btApp.Quit(0); } catch { }
                    ReleaseComObject(_btApp);
                    _btApp = null;
                }
            }
            catch { }
            finally
            {
                _connected = false;
                _offlineMode = false;
                _operationLock.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (Thread.CurrentThread.ManagedThreadId == _staThreadId)
            {
                DisconnectCore();
                _staQueue.CompleteAdding();
                return;
            }
            var disconnected = new ManualResetEventSlim(false);
            try
            {
                _staQueue.Add(() =>
                {
                    try { DisconnectCore(); }
                    finally { disconnected.Set(); }
                });
            }
            catch
            {
                disconnected.Set();
            }
            if (!disconnected.Wait(5000))
            {
                LoggerService.Warn("BarTender 断开连接超时，后台 COM 线程将随进程退出。 ");
                try { _staQueue.CompleteAdding(); } catch { }
                return;
            }
            _staQueue.CompleteAdding();
            if (Thread.CurrentThread.ManagedThreadId != _staThreadId && _staThread.IsAlive)
                _staThread.Join(5000);
            _operationLock.Dispose();
            _staQueue.Dispose();
        }
    }

    public class PrintResult
    {
        public bool Success { get; }
        public string ErrorMessage { get; }
        public string DiagnosticDetails { get; }
        public PrintResult(bool success, string msg, string diagnostics = "") { Success = success; ErrorMessage = msg ?? ""; DiagnosticDetails = diagnostics ?? ""; }
    }
}
