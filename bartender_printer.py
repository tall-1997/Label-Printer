#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
BarTender 标签打印工具 v2.2.7
集成 BarTender 2022 R2 Enterprise，IMEI 标签打印
"""

import os
import sys
import csv
import json
import tkinter as tk
from tkinter import ttk, messagebox, filedialog
from datetime import datetime
import threading

try:
    import win32com.client
    import pythoncom
    HAS_WIN32COM = True
except ImportError:
    HAS_WIN32COM = False

try:
    import openpyxl
    HAS_OPENPYXL = True
except ImportError:
    HAS_OPENPYXL = False


class PrintRecord:
    def __init__(self, imei, print_time, status="PASS"):
        self.imei = str(imei) if imei else ""
        self.print_time = str(print_time) if print_time else ""
        self.status = str(status) if status else "PASS"


class BarTenderPrintApp:
    VERSION = "v2.2.7"

    def __init__(self):
        self.root = tk.Tk()
        self.root.title(f"BarTender 标签打印工具 {self.VERSION}")
        self.root.geometry("950x750")
        self.root.minsize(850, 700)

        # 配置路径
        self.app_dir = os.path.join(os.path.expanduser("~"), ".bartender-printer")
        os.makedirs(self.app_dir, exist_ok=True)
        self.config_file = os.path.join(self.app_dir, "bt_config.json")
        self.records_file = os.path.join(self.app_dir, "print_records.csv")

        # BarTender
        self.bt_app = None
        self.bt_format = None

        # 数据
        self.print_records = []
        self.excel_data = []

        # 配置
        self._config = {
            'template_path': '',
            'datasource': 'IMEI1',
            'excel_path': '',
            'excel_column': 'IMEI1',
            'printer': '',
            'verify_excel': True,
        }

        # 加载
        self._load_config()
        self._load_records()

        # UI变量（在tk.Tk()之后创建）
        self.template_path_var = tk.StringVar(value=self._config['template_path'])
        self.datasource_var = tk.StringVar(value=self._config['datasource'])
        self.excel_path_var = tk.StringVar(value=self._config['excel_path'])
        self.excel_column_var = tk.StringVar(value=self._config['excel_column'])
        self.printer_var = tk.StringVar(value=self._config['printer'])
        self.verify_excel_var = tk.BooleanVar(value=self._config['verify_excel'])

        # 创建UI
        self._create_ui()
        self.refresh_history()
        self.refresh_stats()

        # 加载Excel数据
        if self._config['excel_path'] and os.path.exists(self._config['excel_path']):
            self.root.after(100, self._load_excel_data)

        self.root.protocol("WM_DELETE_WINDOW", self._on_closing)

    def _load_config(self):
        try:
            if os.path.exists(self.config_file):
                with open(self.config_file, 'r', encoding='utf-8') as f:
                    self._config.update(json.load(f))
        except Exception:
            pass

    def _save_config(self):
        try:
            self._config.update({
                'template_path': self.template_path_var.get(),
                'datasource': self.datasource_var.get(),
                'excel_path': self.excel_path_var.get(),
                'excel_column': self.excel_column_var.get(),
                'printer': self.printer_var.get(),
                'verify_excel': self.verify_excel_var.get(),
            })
            with open(self.config_file, 'w', encoding='utf-8') as f:
                json.dump(self._config, f, ensure_ascii=False, indent=2)
        except Exception:
            pass

    def _load_records(self):
        try:
            if os.path.exists(self.records_file):
                with open(self.records_file, 'r', encoding='utf-8-sig') as f:
                    reader = csv.reader(f)
                    next(reader, None)
                    for row in reader:
                        if len(row) >= 3:
                            self.print_records.append(PrintRecord(row[0], row[1], row[2]))
        except Exception:
            pass

    def _save_records(self):
        try:
            with open(self.records_file, 'w', newline='', encoding='utf-8-sig') as f:
                writer = csv.writer(f)
                writer.writerow(['imei', 'print_time', 'status'])
                for r in self.print_records:
                    writer.writerow([r.imei, r.print_time, r.status])
        except Exception:
            pass

    def _create_ui(self):
        main_frame = ttk.Frame(self.root, padding="10")
        main_frame.pack(fill=tk.BOTH, expand=True)

        # 标题
        title_frame = ttk.Frame(main_frame)
        title_frame.pack(fill=tk.X, pady=(0, 10))
        ttk.Label(title_frame, text="BarTender 标签打印工具", font=("微软雅黑", 16, "bold")).pack(side=tk.LEFT)
        ttk.Button(title_frame, text="设置", command=self._open_settings).pack(side=tk.RIGHT)

        # 选项卡
        self.notebook = ttk.Notebook(main_frame)
        self.notebook.pack(fill=tk.BOTH, expand=True)

        self._create_print_tab()
        self._create_history_tab()
        self._create_stats_tab()

        # 状态栏
        self.status_var = tk.StringVar(value="就绪")
        ttk.Label(main_frame, textvariable=self.status_var, relief=tk.SUNKEN).pack(fill=tk.X, pady=(10, 0))

        # 初始化BarTender
        self._init_bartender()

    def _create_print_tab(self):
        tab = ttk.Frame(self.notebook, padding="10")
        self.notebook.add(tab, text="打印")

        # 模板
        tf = ttk.LabelFrame(tab, text="BarTender 模板", padding="10")
        tf.pack(fill=tk.X, pady=(0, 10))
        ff = ttk.Frame(tf)
        ff.pack(fill=tk.X, pady=(0, 5))
        ttk.Label(ff, text="模板文件：", width=12).pack(side=tk.LEFT)
        ttk.Entry(ff, textvariable=self.template_path_var, state="readonly").pack(side=tk.LEFT, fill=tk.X, expand=True, padx=(0, 5))
        ttk.Button(ff, text="浏览", command=self._browse_template).pack(side=tk.RIGHT)
        df = ttk.Frame(tf)
        df.pack(fill=tk.X)
        ttk.Label(df, text="数据源名称：", width=12).pack(side=tk.LEFT)
        ttk.Entry(df, textvariable=self.datasource_var).pack(side=tk.LEFT, fill=tk.X, expand=True)

        # Excel
        ef = ttk.LabelFrame(tab, text="IMEI 数据源（Excel）", padding="10")
        ef.pack(fill=tk.X, pady=(0, 10))
        ef1 = ttk.Frame(ef)
        ef1.pack(fill=tk.X, pady=(0, 5))
        ttk.Label(ef1, text="Excel 文件：", width=12).pack(side=tk.LEFT)
        ttk.Entry(ef1, textvariable=self.excel_path_var, state="readonly").pack(side=tk.LEFT, fill=tk.X, expand=True, padx=(0, 5))
        ttk.Button(ef1, text="选择文件", command=self._browse_excel).pack(side=tk.RIGHT)
        ef2 = ttk.Frame(ef)
        ef2.pack(fill=tk.X)
        ttk.Label(ef2, text="IMEI 列名：", width=12).pack(side=tk.LEFT)
        ttk.Entry(ef2, textvariable=self.excel_column_var).pack(side=tk.LEFT, fill=tk.X, expand=True, padx=(0, 10))
        ttk.Label(ef2, text="已加载：").pack(side=tk.LEFT)
        self.excel_count_var = tk.StringVar(value="0 条")
        ttk.Label(ef2, textvariable=self.excel_count_var).pack(side=tk.LEFT)

        # 打印机
        pf = ttk.LabelFrame(tab, text="打印机", padding="10")
        pf.pack(fill=tk.X, pady=(0, 10))
        pf2 = ttk.Frame(pf)
        pf2.pack(fill=tk.X)
        ttk.Label(pf2, text="选择打印机：", width=12).pack(side=tk.LEFT)
        self.printer_combo = ttk.Combobox(pf2, textvariable=self.printer_var, state="readonly")
        self.printer_combo.pack(side=tk.LEFT, fill=tk.X, expand=True, padx=(0, 5))
        ttk.Button(pf2, text="刷新", command=self._refresh_printers).pack(side=tk.RIGHT)

        # 选项
        of = ttk.Frame(tab)
        of.pack(fill=tk.X, pady=(0, 10))
        ttk.Checkbutton(of, text="打印前校验 Excel 数据", variable=self.verify_excel_var).pack(side=tk.LEFT)

        # 按钮
        bf = ttk.Frame(tab)
        bf.pack(fill=tk.X, pady=(10, 0))
        ttk.Button(bf, text="输入 IMEI 并打印", command=self._show_imei_dialog).pack(side=tk.LEFT, padx=(0, 10))
        ttk.Button(bf, text="批量导入 IMEI", command=self._import_imei_file).pack(side=tk.LEFT)

        # 状态
        sf = ttk.LabelFrame(tab, text="打印状态", padding="10")
        sf.pack(fill=tk.BOTH, expand=True, pady=(10, 0))
        self.print_status = tk.Text(sf, state=tk.DISABLED, wrap=tk.WORD)
        sb = ttk.Scrollbar(sf, orient=tk.VERTICAL, command=self.print_status.yview)
        self.print_status.configure(yscrollcommand=sb.set)
        sb.pack(side=tk.RIGHT, fill=tk.Y)
        self.print_status.pack(fill=tk.BOTH, expand=True)

    def _create_history_tab(self):
        tab = ttk.Frame(self.notebook, padding="10")
        self.notebook.add(tab, text="历史记录")

        hf = ttk.LabelFrame(tab, text="打印记录", padding="10")
        hf.pack(fill=tk.BOTH, expand=True)
        self.history_tree = ttk.Treeview(hf, columns=("imei", "time", "status"), show="headings")
        self.history_tree.heading("imei", text="IMEI")
        self.history_tree.heading("time", text="打印时间")
        self.history_tree.heading("status", text="状态")
        self.history_tree.pack(fill=tk.BOTH, expand=True)

        bf = ttk.Frame(tab)
        bf.pack(fill=tk.X, pady=(10, 0))
        ttk.Button(bf, text="清空记录", command=self._clear_records).pack(side=tk.LEFT)

    def _create_stats_tab(self):
        tab = ttk.Frame(self.notebook, padding="10")
        self.notebook.add(tab, text="统计")

        sf = ttk.Frame(tab)
        sf.pack(fill=tk.X, pady=(0, 20))

        c1 = ttk.LabelFrame(sf, text="今日打印", padding="15")
        c1.pack(side=tk.LEFT, fill=tk.X, expand=True, padx=(0, 10))
        self.today_count_var = tk.StringVar(value="0")
        ttk.Label(c1, textvariable=self.today_count_var, font=("微软雅黑", 24, "bold")).pack()
        ttk.Label(c1, text="个 IMEI").pack()

        c2 = ttk.LabelFrame(sf, text="总打印", padding="15")
        c2.pack(side=tk.LEFT, fill=tk.X, expand=True)
        self.total_count_var = tk.StringVar(value="0")
        ttk.Label(c2, textvariable=self.total_count_var, font=("微软雅黑", 24, "bold")).pack()
        ttk.Label(c2, text="个 IMEI").pack()

    def _init_bartender(self):
        if not HAS_WIN32COM:
            self.status_var.set("警告：未安装 pywin32，BarTender 功能不可用")
            return
        try:
            pythoncom.CoInitialize()
            print("[DEBUG] 正在创建 BarTender.Application...")
            self.bt_app = win32com.client.Dispatch("BarTender.Application")
            print(f"[DEBUG] BarTender 对象: {self.bt_app}")
            self.bt_app.Visible = False
            print("[DEBUG] Visible 设置完成")
            self.status_var.set("BarTender 已连接")
            self._refresh_printers()
        except Exception as e:
            print(f"[DEBUG] BarTender 初始化失败: {e}")
            self.status_var.set(f"BarTender 连接失败: {e}")

    def _refresh_printers(self):
        printers = []
        try:
            if sys.platform == 'win32':
                import win32print
                printers = [p[2] for p in win32print.EnumPrinters(win32print.PRINTER_ENUM_LOCAL | win32print.PRINTER_ENUM_CONNECTIONS)]
        except Exception:
            pass
        self.printer_combo['values'] = printers
        if printers:
            self.printer_combo.current(0)

    def _browse_template(self):
        path = filedialog.askopenfilename(title="选择 BarTender 模板", filetypes=[("BarTender 文件", "*.btw"), ("所有文件", "*.*")])
        if path:
            self.template_path_var.set(path)
            self._save_config()

    def _browse_excel(self):
        path = filedialog.askopenfilename(title="选择 Excel 文件", filetypes=[("Excel 文件", "*.xlsx *.xls"), ("CSV 文件", "*.csv")])
        if path:
            self.excel_path_var.set(path)
            self._save_config()
            self._load_excel_data()

    def _load_excel_data(self):
        path = self.excel_path_var.get()
        if not path or not os.path.exists(path):
            self.excel_data = []
            self.excel_count_var.set("0 条")
            return
        try:
            col = self.excel_column_var.get().strip()
            if not col:
                return
            if path.endswith('.csv'):
                with open(path, 'r', encoding='utf-8-sig') as f:
                    reader = csv.DictReader(f)
                    self.excel_data = [row.get(col, '').strip() for row in reader if row.get(col, '').strip()]
            elif HAS_OPENPYXL:
                wb = openpyxl.load_workbook(path, read_only=True)
                ws = wb.active
                header = [c.value for c in ws[1]]
                if col not in header:
                    wb.close()
                    return
                idx = header.index(col)
                self.excel_data = []
                for row in ws.iter_rows(min_row=2, values_only=True):
                    if idx < len(row) and row[idx]:
                        self.excel_data.append(str(row[idx]).strip())
                wb.close()
            self.excel_count_var.set(f"{len(self.excel_data)} 条")
        except Exception:
            self.excel_data = []
            self.excel_count_var.set("0 条")

    def _is_imei_in_excel(self, imei):
        if not self.excel_data:
            return True
        return str(imei).strip() in self.excel_data

    def _is_imei_printed(self, imei):
        return any(r.imei == str(imei).strip() for r in self.print_records)

    def _show_imei_dialog(self):
        dialog = tk.Toplevel(self.root)
        dialog.title("输入 IMEI")
        dialog.geometry("400x200")
        dialog.transient(self.root)
        dialog.grab_set()

        main_frame = ttk.Frame(dialog, padding="20")
        main_frame.pack(fill=tk.BOTH, expand=True)

        ttk.Label(main_frame, text="输入要打印的 IMEI（每行一个）：").pack(anchor=tk.W, pady=(0, 10))

        imei_text = tk.Text(main_frame, wrap=tk.WORD, height=3)
        imei_text.pack(fill=tk.X, pady=(0, 10))
        imei_text.focus_set()

        def on_content_change(event=None):
            lines = imei_text.get("1.0", tk.END).count('\n') + 1
            imei_text.config(height=min(max(lines, 3), 10))

        imei_text.bind('<KeyRelease>', on_content_change)

        btn_frame = ttk.Frame(main_frame)
        btn_frame.pack(fill=tk.X)

        def on_print():
            content = imei_text.get("1.0", tk.END).strip()
            if content:
                imei_list = [l.strip() for l in content.split('\n') if l.strip()]
                dialog.destroy()
                self._process_imei_list(imei_list)

        def on_enter(event):
            content = imei_text.get("1.0", tk.END).strip()
            if content:
                lines = [l.strip() for l in content.split('\n') if l.strip()]
                if len(lines) == 1:
                    on_print()

        imei_text.bind('<Return>', on_enter)
        ttk.Button(btn_frame, text="打印", command=on_print).pack(side=tk.RIGHT, padx=(5, 0))
        ttk.Button(btn_frame, text="取消", command=dialog.destroy).pack(side=tk.RIGHT)

    def _import_imei_file(self):
        path = filedialog.askopenfilename(title="选择 IMEI 文件", filetypes=[("文本文件", "*.txt"), ("CSV 文件", "*.csv")])
        if path:
            try:
                with open(path, 'r', encoding='utf-8') as f:
                    content = f.read()
                imei_list = [l.strip() for l in content.split('\n') if l.strip()]
                if imei_list:
                    self._process_imei_list(imei_list)
            except Exception as e:
                messagebox.showerror("错误", f"文件读取失败: {e}")

    def _process_imei_list(self, imei_list):
        printer = self.printer_var.get()
        if not printer:
            self._update_status("错误：请选择打印机", "error")
            return

        template_path = self.template_path_var.get()
        if not template_path or not os.path.exists(template_path):
            self._update_status("错误：请选择有效的 BarTender 模板文件", "error")
            return

        if not self.bt_app:
            self._update_status("错误：BarTender 未连接", "error")
            return

        datasource = self.datasource_var.get()

        # 校验Excel
        if self.verify_excel_var.get() and self.excel_data:
            invalid = [i for i in imei_list if not self._is_imei_in_excel(i)]
            if invalid:
                result = messagebox.askyesnocancel(
                    "数据不在文件中",
                    f"发现 {len(invalid)} 个 IMEI 不在 Excel 数据中！\n\n"
                    f"无效 IMEI:\n{chr(10).join(invalid[:5])}\n\n"
                    "是：继续打印 / 否：跳过无效 / 取消：取消打印"
                )
                if result is None:
                    return
                elif not result:
                    imei_list = [i for i in imei_list if self._is_imei_in_excel(i)]
                    if not imei_list:
                        self._update_status("没有有效的 IMEI", "error")
                        return

        # 检查已打印
        printed = [i for i in imei_list if self._is_imei_printed(i)]
        if printed:
            result = messagebox.askyesnocancel(
                "数据重复",
                f"发现 {len(printed)} 个 IMEI 已打印过！\n\n"
                f"已打印:\n{chr(10).join(printed[:5])}\n\n"
                "是：继续打印 / 否：跳过已打印 / 取消：取消打印"
            )
            if result is None:
                return
            elif not result:
                imei_list = [i for i in imei_list if not self._is_imei_printed(i)]
                if not imei_list:
                    self._update_status("所有 IMEI 都已打印过", "error")
                    return

        # 后台打印
        self._clear_status()
        self._update_status(f"开始打印 {len(imei_list)} 个 IMEI...", "info")
        threading.Thread(target=self._do_print, args=(imei_list, template_path, printer, datasource), daemon=True).start()

    def _do_print(self, imei_list, template_path, printer, datasource):
        """后台打印线程"""
        # 线程必须初始化 COM
        pythoncom.CoInitialize()
        bt_app = None
        try:
            # 在线程中重新创建 BarTender 对象
            print("[DEBUG] 线程中创建 BarTender 对象...")
            bt_app = win32com.client.Dispatch("BarTender.Application")
            bt_app.Visible = False
            print("[DEBUG] 线程中 BarTender 对象创建成功")
            
            self._do_print_inner(bt_app, imei_list, template_path, printer, datasource)
        except Exception as e:
            print(f"[DEBUG] 线程异常: {e}")
            self.root.after(0, lambda: self._update_status(f"线程错误: {e}", "error"))
        finally:
            # 关闭 BarTender
            if bt_app:
                try:
                    bt_app.Quit()
                except:
                    pass
            pythoncom.CoUninitialize()
    
    def _do_print_inner(self, bt_app, imei_list, template_path, printer, datasource):
        """实际打印逻辑"""
        ok = 0
        fail = 0
        for imei in imei_list:
            success, error_msg = self._print_single(bt_app, imei, template_path, printer, datasource)
            now = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
            if success:
                self.print_records.append(PrintRecord(imei, now, "PASS"))
                ok += 1
                self.root.after(0, lambda m=f"PASS {imei}": self._update_status(m, "success"))
            else:
                self.print_records.append(PrintRecord(imei, now, "FAIL"))
                fail += 1
                self.root.after(0, lambda m=f"FAIL {imei} - {error_msg}": self._update_status(m, "error"))

        self._save_config()
        self._save_records()
        self.root.after(0, self.refresh_history)
        self.root.after(0, self.refresh_stats)
        self.root.after(0, lambda: self._update_status(f"\n完成：成功 {ok}，失败 {fail}", "info"))
        self.root.after(0, lambda: self.status_var.set(f"完成：成功 {ok}，失败 {fail}"))

    def _print_single(self, bt_app, imei, template_path, printer, datasource):
        """打印单个IMEI"""
        bt_format = None
        try:
            print(f"[DEBUG] 准备打开模板: {template_path}")
            
            # 打开模板 - 使用正确的参数
            bt_format = bt_app.Formats.Open(template_path, False)
            print(f"[DEBUG] 模板打开成功")
            
            # 设置数据源
            bt_format.SetNamedSubStringValue(datasource, str(imei))
            print(f"[DEBUG] 数据源设置成功: {datasource}={imei}")
            
            # 设置打印机
            bt_format.Printer = printer
            print(f"[DEBUG] 打印机设置成功: {printer}")
            
            # 打印
            try:
                bt_format.PrintOut(False, False)
                print(f"[DEBUG] PrintOut 执行完成")
            except Exception as e:
                print(f"[DEBUG] PrintOut 异常(通常可忽略): {e}")
            
            # 关闭模板
            bt_format.Close()
            print(f"[DEBUG] 模板关闭成功")
            
            return True, ""
                
        except Exception as e:
            error_msg = str(e)
            print(f"[DEBUG] 打印失败: {error_msg}")
            if bt_format:
                try:
                    bt_format.Close()
                except:
                    pass
            return False, error_msg

    def _clear_status(self):
        self.print_status.config(state=tk.NORMAL)
        self.print_status.delete("1.0", tk.END)
        self.print_status.config(state=tk.DISABLED)

    def _update_status(self, msg, level="info"):
        self.print_status.config(state=tk.NORMAL)
        self.print_status.tag_configure("success", foreground="green")
        self.print_status.tag_configure("error", foreground="red")
        self.print_status.tag_configure("info", foreground="black")
        tag = level if level in ("success", "error", "info") else "info"
        self.print_status.insert(tk.END, msg + "\n", tag)
        self.print_status.see(tk.END)
        self.print_status.config(state=tk.DISABLED)

    def refresh_history(self):
        for item in self.history_tree.get_children():
            self.history_tree.delete(item)
        for r in reversed(self.print_records):
            self.history_tree.insert('', 0, values=(r.imei, r.print_time, r.status))

    def refresh_stats(self):
        today = datetime.now().strftime("%Y-%m-%d")
        today_count = sum(1 for r in self.print_records if r.print_time.startswith(today))
        self.today_count_var.set(str(today_count))
        self.total_count_var.set(str(len(self.print_records)))

    def _clear_records(self):
        if messagebox.askyesno("确认", "确定要清空所有记录吗？"):
            self.print_records.clear()
            self._save_records()
            self.refresh_history()
            self.refresh_stats()

    def _open_settings(self):
        dialog = tk.Toplevel(self.root)
        dialog.title("设置")
        dialog.geometry("400x250")
        dialog.transient(self.root)
        dialog.grab_set()

        f = ttk.Frame(dialog, padding="20")
        f.pack(fill=tk.BOTH, expand=True)

        ttk.Label(f, text="设置", font=("微软雅黑", 12, "bold")).pack(anchor=tk.W, pady=(0, 15))

        ttk.Label(f, text="默认打印机：").pack(anchor=tk.W)
        printer_var = tk.StringVar(value=self.printer_var.get())
        ttk.Entry(f, textvariable=printer_var).pack(fill=tk.X, pady=(0, 10))

        ttk.Label(f, text="数据源名称：").pack(anchor=tk.W)
        ds_var = tk.StringVar(value=self.datasource_var.get())
        ttk.Entry(f, textvariable=ds_var).pack(fill=tk.X, pady=(0, 10))

        vf = tk.BooleanVar(value=self.verify_excel_var.get())
        ttk.Checkbutton(f, text="打印前校验 Excel 数据", variable=vf).pack(anchor=tk.W, pady=(0, 15))

        bf = ttk.Frame(f)
        bf.pack(fill=tk.X)

        def save():
            self.printer_var.set(printer_var.get())
            self.datasource_var.set(ds_var.get())
            self.verify_excel_var.set(vf.get())
            self._save_config()
            dialog.destroy()

        ttk.Button(bf, text="保存", command=save).pack(side=tk.RIGHT, padx=(5, 0))
        ttk.Button(bf, text="取消", command=dialog.destroy).pack(side=tk.RIGHT)

    def _on_closing(self):
        self._save_config()
        self._save_records()
        if self.bt_app:
            try:
                self.bt_app.Quit()
            except:
                pass
        if HAS_WIN32COM:
            try:
                pythoncom.CoUninitialize()
            except:
                pass
        self.root.destroy()

    def run(self):
        self.root.mainloop()


def main():
    app = BarTenderPrintApp()
    app.run()


if __name__ == "__main__":
    main()
