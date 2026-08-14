using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace BarTenderPreviewHost
{
    [DataContract]
    internal sealed class PreviewRequest
    {
        [DataMember] public string SdkPath { get; set; }
        [DataMember] public string TemplatePath { get; set; }
        [DataMember] public string OutputPath { get; set; }
        [DataMember] public Dictionary<string, string> Fields { get; set; }
    }

    internal static class Program
    {
        private static Assembly _sdkAssembly;
        private static string _sdkDirectory;

        [STAThread]
        private static int Main(string[] args)
        {
            var probe = args.Length > 0 && string.Equals(args[0], "--probe", StringComparison.OrdinalIgnoreCase);
            var errorPath = probe
                ? (args.Length > 2 ? args[2] : "")
                : (args.Length > 1 ? args[1] : "");
            try
            {
                if (probe)
                {
                    if (args.Length < 3) throw new ArgumentException("缺少 SDK 或探针错误输出路径");
                    LoadSdk(args[1]);
                    ProbeSdk();
                    return 0;
                }
                if (args.Length < 2) throw new ArgumentException("缺少预览请求或错误输出路径");
                var request = ReadRequest(args[0]);
                ValidateRequest(request);
                LoadSdk(request.SdkPath);
                ExportPreview(request);
                return 0;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(errorPath, FormatException(ex)); } catch { }
                return 1;
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyResolve -= ResolveSdkAssembly;
            }
        }

        private static void LoadSdk(string sdkPath)
        {
            if (!File.Exists(sdkPath)) throw new FileNotFoundException("BarTender SDK 不存在", sdkPath);
            _sdkDirectory = Path.GetDirectoryName(Path.GetFullPath(sdkPath));
            AppDomain.CurrentDomain.AssemblyResolve += ResolveSdkAssembly;
            _sdkAssembly = Assembly.LoadFrom(sdkPath);
        }

        private static void ProbeSdk()
        {
            var documentType = GetSdkType("LabelFormatDocument");
            var parameterTypes = new[]
            {
                typeof(string), GetSdkType("ImageType"), GetSdkType("ColorDepth"),
                GetSdkType("Resolution"), GetSdkType("OverwriteOptions")
            };
            if (documentType.GetMethod("ExportImageToFile", parameterTypes) == null)
                throw new MissingMethodException(documentType.FullName, "ExportImageToFile");
            var engine = Activator.CreateInstance(GetSdkType("Engine"));
            try { Invoke(engine, "Start"); }
            finally
            {
                if (engine != null)
                {
                    try { Invoke(engine, "Stop"); } catch { }
                }
            }
        }

        private static PreviewRequest ReadRequest(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                var settings = new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true };
                return (PreviewRequest)new DataContractJsonSerializer(typeof(PreviewRequest), settings).ReadObject(stream);
            }
        }

        private static void ValidateRequest(PreviewRequest request)
        {
            if (request == null) throw new InvalidDataException("预览请求为空");
            if (!File.Exists(request.SdkPath)) throw new FileNotFoundException("BarTender SDK 不存在", request.SdkPath);
            if (!File.Exists(request.TemplatePath)) throw new FileNotFoundException("模板不存在", request.TemplatePath);
            if (string.IsNullOrWhiteSpace(request.OutputPath)) throw new InvalidDataException("预览输出路径为空");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.OutputPath)));
        }

        private static void ExportPreview(PreviewRequest request)
        {
            var fields = request.Fields ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (fields.Count == 0)
            {
                ExportThumbnail(request.TemplatePath, request.OutputPath);
                return;
            }

            object engine = null;
            object document = null;
            try
            {
                engine = Activator.CreateInstance(GetSdkType("Engine"));
                Invoke(engine, "Start");
                var documents = GetProperty(engine, "Documents");
                document = Invoke(documents, "Open", request.TemplatePath);
                var subStrings = GetProperty(document, "SubStrings");
                var available = GetDataSourceNames(subStrings);
                var projected = fields.Where(item => available.Contains(item.Key)).ToList();
                if (projected.Count == 0)
                {
                    CloseDocument(document);
                    document = null;
                    ExportThumbnail(request.TemplatePath, request.OutputPath);
                    return;
                }
                foreach (var item in projected)
                    Invoke(subStrings, "SetSubString", item.Key, item.Value ?? "");

                var resolution = Activator.CreateInstance(GetSdkType("Resolution"), 300, 300);
                Invoke(document, "ExportImageToFile", request.OutputPath,
                    Enum.Parse(GetSdkType("ImageType"), "PNG"),
                    Enum.Parse(GetSdkType("ColorDepth"), "ColorDepth24bit"),
                    resolution,
                    Enum.Parse(GetSdkType("OverwriteOptions"), "Overwrite"));
                ValidateImage(request.OutputPath);
            }
            finally
            {
                CloseDocument(document);
                if (engine != null)
                {
                    try { Invoke(engine, "Stop"); } catch { }
                }
            }
        }

        private static void ExportThumbnail(string templatePath, string outputPath)
        {
            var type = GetSdkType("LabelFormatThumbnail");
            var create = type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(string), typeof(Color), typeof(int), typeof(int) }, null)
                ?? throw new MissingMethodException(type.FullName, "Create");
            using (var image = create.Invoke(null, new object[] { templatePath, Color.White, 1200, 1200 }) as Image
                ?? throw new InvalidDataException("BarTender SDK 未生成模板缩略图"))
                image.Save(outputPath, ImageFormat.Png);
            ValidateImage(outputPath);
        }

        private static HashSet<string> GetDataSourceNames(object subStrings)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (subStrings is IEnumerable items)
            {
                foreach (var item in items) AddName(result, item);
                return result;
            }
            var count = Convert.ToInt32(GetProperty(subStrings, "Count"));
            var itemProperty = subStrings.GetType().GetProperties()
                .First(property => property.Name == "Item" &&
                    property.GetIndexParameters().Length == 1 &&
                    property.GetIndexParameters()[0].ParameterType == typeof(int));
            var oneBased = false;
            if (count > 0)
            {
                try { AddName(result, itemProperty.GetValue(subStrings, new object[] { 0 })); }
                catch { oneBased = true; }
            }
            for (var index = 0; index < count; index++)
            {
                if (!oneBased && index == 0) continue;
                AddName(result, itemProperty.GetValue(subStrings, new object[] { index + (oneBased ? 1 : 0) }));
            }
            return result;
        }

        private static void AddName(ISet<string> names, object item)
        {
            var name = item?.GetType().GetProperty("Name")?.GetValue(item)?.ToString();
            if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
        }

        private static void CloseDocument(object document)
        {
            if (document == null || _sdkAssembly == null) return;
            try { Invoke(document, "Close", Enum.Parse(GetSdkType("SaveOptions"), "DoNotSaveChanges")); } catch { }
        }

        private static Type GetSdkType(string name) =>
            _sdkAssembly.GetType("Seagull.BarTender.Print." + name, true);

        private static object GetProperty(object target, string name) =>
            target.GetType().GetProperty(name)?.GetValue(target) ?? throw new MissingMemberException(target.GetType().FullName, name);

        private static object Invoke(object target, string name, params object[] arguments)
        {
            try
            {
                return target.GetType().InvokeMember(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.InvokeMethod,
                    null, target, arguments);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }

        private static Assembly ResolveSdkAssembly(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name).Name;
            var path = Path.Combine(_sdkDirectory ?? "", name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }

        private static void ValidateImage(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length == 0) throw new InvalidDataException("预览图片为空");
            using (var image = Image.FromFile(path))
            {
                if (image.Width <= 0 || image.Height <= 0) throw new InvalidDataException("预览图片尺寸无效");
            }
        }

        private static string FormatException(Exception exception)
        {
            var root = exception;
            while (root.InnerException != null) root = root.InnerException;
            return root.GetType().FullName + ": " + root.Message;
        }
    }
}
