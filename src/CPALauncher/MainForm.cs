using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using CPALauncher.Models;
using CPALauncher.Services;

namespace CPALauncher;

public sealed class MainForm : Form
{
	private readonly IContainer components = new Container();

	private readonly LauncherSettingsStore settingsStore = new LauncherSettingsStore();

	private readonly CpaConfigInspector configInspector = new CpaConfigInspector();

	private readonly CpaProcessManager processManager = new CpaProcessManager();

	private readonly WindowsStartupManager startupManager = new WindowsStartupManager();

	private readonly HttpClient httpClient = new HttpClient
	{
		Timeout = TimeSpan.FromSeconds(2.0)
	};

	private readonly System.Windows.Forms.Timer refreshTimer = new System.Windows.Forms.Timer
	{
		Interval = 2500
	};

	private readonly Label lblStatusBadge = new Label();

	private readonly Label lblStatusDetail = new Label();

	private readonly Label lblManagedPidValue = new Label();

	private readonly TextBox txtExecutablePath = new TextBox();

	private readonly TextBox txtConfigPath = new TextBox();

	private readonly TextBox txtManagementUrl = new TextBox();

	private readonly TextBox txtProbeUrl = new TextBox();

	private readonly TextBox txtConfigDirectory = new TextBox();

	private readonly TextBox txtLogDirectory = new TextBox();

	private readonly TextBox txtAuthDirectory = new TextBox();

	private readonly TextBox txtDiagnostics = new TextBox();

	private readonly NumericUpDown nudAutoStartDelaySeconds = new NumericUpDown();

	private readonly Label lblSettingsNote = new Label();

	private readonly CheckBox chkMinimizeToTray = new CheckBox();

	private readonly CheckBox chkAutoStartService = new CheckBox();

	private readonly CheckBox chkLaunchLauncherOnWindowsStartup = new CheckBox();

	private readonly CheckBox chkOpenManagementAfterStart = new CheckBox();

	private readonly Button btnStart = new Button();

	private readonly Button btnStop = new Button();

	private readonly Button btnOpenManagement = new Button();

	private readonly Button btnOpenLogsDir = new Button();

	private readonly Button btnRefresh = new Button();

	private readonly Button btnConfigureWizard = new Button();

	private readonly Button btnBrowseExecutable = new Button();

	private readonly Button btnBrowseConfig = new Button();

	private readonly Button btnOpenExecutableDir = new Button();

	private readonly Button btnOpenConfigDir = new Button();

	private readonly Button btnCopyDiagnostics = new Button();

	private readonly Button btnExportDiagnostics = new Button();

	private readonly Button btnClearDiagnostics = new Button();

	private readonly NotifyIcon trayIcon;

	private readonly ContextMenuStrip trayMenu;

	private readonly ToolStripMenuItem trayShowMenuItem;

	private readonly ToolStripMenuItem trayStartMenuItem;

	private readonly ToolStripMenuItem trayStopMenuItem;

	private readonly ToolStripMenuItem trayOpenManagementMenuItem;

	private readonly ToolStripMenuItem trayExitMenuItem;

	private LauncherSettings settings = new LauncherSettings();

	private CpaRuntimeInfo? runtimeInfo;

	private LauncherStatus currentStatus = LauncherStatus.Unconfigured;

	private string? lastFailureMessage;

	private bool suppressSettingSaves;

	private bool isRefreshing;

	private bool isExiting;

	private bool initialPromptShown;

	private bool pendingOpenManagementAfterStart;

	private bool trayHintShown;

	private readonly bool startMinimizedToTray;

	public MainForm(bool startMinimizedToTray = false)
	{
		this.startMinimizedToTray = startMinimizedToTray;
		trayMenu = new ContextMenuStrip(components);
		trayShowMenuItem = new ToolStripMenuItem("显示主窗口");
		trayStartMenuItem = new ToolStripMenuItem("启动 CPA");
		trayStopMenuItem = new ToolStripMenuItem("停止 CPA");
		trayOpenManagementMenuItem = new ToolStripMenuItem("打开管理页");
		trayExitMenuItem = new ToolStripMenuItem("退出启动器");
		trayMenu.Items.AddRange(new ToolStripItem[6]
		{
			trayShowMenuItem,
			trayStartMenuItem,
			trayStopMenuItem,
			trayOpenManagementMenuItem,
			new ToolStripSeparator(),
			trayExitMenuItem
		});
		trayIcon = new NotifyIcon(components)
		{
			Icon = SystemIcons.Application,
			Text = "CPA Launcher",
			Visible = true,
			ContextMenuStrip = trayMenu
		};
		InitializeUi();
		InitializeFormState();
		WireEvents();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			httpClient.Dispose();
			refreshTimer.Dispose();
			processManager.Dispose();
			components.Dispose();
		}
		base.Dispose(disposing);
	}

	private void InitializeUi()
	{
		SuspendLayout();
		Text = "CPA Launcher";
		base.StartPosition = FormStartPosition.CenterScreen;
		MinimumSize = new Size(1120, 860);
		base.Size = new Size(1280, 940);
		Font = new Font("Segoe UI", 9f);
		base.AutoScaleMode = AutoScaleMode.Dpi;
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(18, 16, 18, 16),
			ColumnCount = 1,
			RowCount = 5
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 124f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 184f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 172f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 380f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		base.Controls.Add(tableLayoutPanel);
		tableLayoutPanel.Controls.Add(BuildHeader(), 0, 0);
		tableLayoutPanel.Controls.Add(BuildSummaryRow(), 0, 1);
		tableLayoutPanel.Controls.Add(BuildPathsGroup(), 0, 2);
		tableLayoutPanel.Controls.Add(BuildInfoOptionsRow(), 0, 3);
		tableLayoutPanel.Controls.Add(BuildDiagnosticsGroup(), 0, 4);
		ResumeLayout(performLayout: false);
	}

	private Control BuildHeader()
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 2,
			Padding = new Padding(4, 6, 4, 0)
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle());
		tableLayoutPanel.RowStyles.Add(new RowStyle());
		Label control = new Label
		{
			AutoSize = true,
			Font = new Font("Segoe UI Semibold", 20f, FontStyle.Bold),
			Text = "CPA Launcher 启动器",
			Margin = new Padding(0)
		};
		Label control2 = new Label
		{
			AutoSize = true,
			MaximumSize = new Size(920, 0),
			Font = new Font("Segoe UI", 10f),
			ForeColor = SystemColors.GrayText,
			Text = "第一次记住 exe 与 config 路径，之后只负责启动、停止、托盘化和打开 CLIProxyAPI 管理页。",
			Margin = new Padding(3, 8, 0, 0)
		};
		tableLayoutPanel.Controls.Add(control, 0, 0);
		tableLayoutPanel.Controls.Add(control2, 0, 1);
		return tableLayoutPanel;
	}

	private Control BuildSummaryRow()
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 1
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));
		tableLayoutPanel.Controls.Add(BuildStatusGroup(), 0, 0);
		tableLayoutPanel.Controls.Add(BuildActionsGroup(), 1, 0);
		return tableLayoutPanel;
	}

	private GroupBox BuildStatusGroup()
	{
		GroupBox groupBox = CreateGroupBox("当前状态");
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 3,
			Padding = new Padding(4, 8, 4, 0)
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle());
		tableLayoutPanel.RowStyles.Add(new RowStyle());
		tableLayoutPanel.RowStyles.Add(new RowStyle());
		lblStatusBadge.AutoSize = true;
		lblStatusBadge.MinimumSize = new Size(152, 52);
		lblStatusBadge.Margin = new Padding(0, 0, 0, 10);
		lblStatusBadge.Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold);
		lblStatusBadge.TextAlign = ContentAlignment.MiddleCenter;
		lblStatusBadge.Padding = new Padding(16, 8, 16, 8);
		lblStatusDetail.AutoSize = true;
		lblStatusDetail.MaximumSize = new Size(460, 0);
		lblStatusDetail.Margin = new Padding(0, 0, 0, 8);
		lblStatusDetail.Font = new Font("Segoe UI", 10f);
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			AutoSize = true,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = false,
			Margin = new Padding(0)
		};
		Label value = new Label
		{
			AutoSize = true,
			Font = new Font("Segoe UI", 10f),
			ForeColor = SystemColors.GrayText,
			Text = "托管进程 PID：",
			Margin = new Padding(0, 4, 6, 0)
		};
		lblManagedPidValue.AutoSize = true;
		lblManagedPidValue.Font = new Font("Segoe UI", 10f);
		lblManagedPidValue.Margin = new Padding(0, 4, 0, 0);
		flowLayoutPanel.Controls.Add(value);
		flowLayoutPanel.Controls.Add(lblManagedPidValue);
		tableLayoutPanel.Controls.Add(lblStatusBadge, 0, 0);
		tableLayoutPanel.Controls.Add(lblStatusDetail, 0, 1);
		tableLayoutPanel.Controls.Add(flowLayoutPanel, 0, 2);
		groupBox.Controls.Add(tableLayoutPanel);
		return groupBox;
	}

	private GroupBox BuildActionsGroup()
	{
		GroupBox groupBox = CreateGroupBox("快捷操作");
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 3,
			RowCount = 2,
			Padding = new Padding(4, 10, 4, 4)
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
		ConfigureActionButton(btnStart, "启动 CPA");
		ConfigureActionButton(btnStop, "停止 CPA");
		ConfigureActionButton(btnOpenManagement, "打开管理页", 160);
		ConfigureActionButton(btnOpenLogsDir, "打开日志目录", 160);
		ConfigureActionButton(btnRefresh, "重新检测");
		ConfigureActionButton(btnConfigureWizard, "配置向导", 160);
		Button[] array = new Button[6] { btnStart, btnStop, btnOpenManagement, btnOpenLogsDir, btnRefresh, btnConfigureWizard };
		foreach (Button button in array)
		{
			button.Dock = DockStyle.Fill;
			button.Margin = new Padding(4);
		}
		tableLayoutPanel.Controls.Add(btnStart, 0, 0);
		tableLayoutPanel.Controls.Add(btnStop, 1, 0);
		tableLayoutPanel.Controls.Add(btnOpenManagement, 2, 0);
		tableLayoutPanel.Controls.Add(btnRefresh, 0, 1);
		tableLayoutPanel.Controls.Add(btnConfigureWizard, 1, 1);
		tableLayoutPanel.Controls.Add(btnOpenLogsDir, 2, 1);
		groupBox.Controls.Add(tableLayoutPanel);
		return groupBox;
	}

	private GroupBox BuildPathsGroup()
	{
		GroupBox groupBox = CreateGroupBox("路径设置");
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 3,
			RowCount = 2,
			Padding = new Padding(0, 6, 10, 0)
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle());
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));
		ConfigureReadOnlyPathTextBox(txtExecutablePath);
		ConfigureReadOnlyPathTextBox(txtConfigPath);
		txtExecutablePath.Dock = DockStyle.Fill;
		txtConfigPath.Dock = DockStyle.Fill;
		txtExecutablePath.Margin = new Padding(0, 6, 10, 6);
		txtConfigPath.Margin = new Padding(0, 6, 10, 6);
		ConfigureMiniButton(btnBrowseExecutable, "选择 exe", 120);
		ConfigureMiniButton(btnBrowseConfig, "选择配置", 120);
		ConfigureMiniButton(btnOpenExecutableDir, "打开目录", 126);
		ConfigureMiniButton(btnOpenConfigDir, "打开目录", 126);
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = false,
			Margin = new Padding(0, 4, 0, 0)
		};
		FlowLayoutPanel flowLayoutPanel2 = new FlowLayoutPanel
		{
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = false,
			Margin = new Padding(0, 4, 0, 0)
		};
		btnBrowseExecutable.Margin = new Padding(0, 0, 8, 0);
		btnBrowseConfig.Margin = new Padding(0, 0, 8, 0);
		btnOpenExecutableDir.Margin = new Padding(0);
		btnOpenConfigDir.Margin = new Padding(0);
		flowLayoutPanel.Controls.Add(btnBrowseExecutable);
		flowLayoutPanel.Controls.Add(btnOpenExecutableDir);
		flowLayoutPanel2.Controls.Add(btnBrowseConfig);
		flowLayoutPanel2.Controls.Add(btnOpenConfigDir);
		tableLayoutPanel.Controls.Add(CreateRowLabel("CPA exe"), 0, 0);
		tableLayoutPanel.Controls.Add(txtExecutablePath, 1, 0);
		tableLayoutPanel.Controls.Add(flowLayoutPanel, 2, 0);
		tableLayoutPanel.Controls.Add(CreateRowLabel("config.yaml"), 0, 1);
		tableLayoutPanel.Controls.Add(txtConfigPath, 1, 1);
		tableLayoutPanel.Controls.Add(flowLayoutPanel2, 2, 1);
		groupBox.Controls.Add(tableLayoutPanel);
		return groupBox;
	}

	private Control BuildInfoOptionsRow()
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 1
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48f));
		tableLayoutPanel.Controls.Add(BuildInfoGroup(), 0, 0);
		tableLayoutPanel.Controls.Add(BuildOptionsGroup(), 1, 0);
		return tableLayoutPanel;
	}

	private GroupBox BuildInfoGroup()
	{
		GroupBox groupBox = CreateGroupBox("推导信息");
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 5,
			Padding = new Padding(0, 4, 8, 0)
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		for (int i = 0; i < 5; i++)
		{
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
		}
		ConfigureReadOnlyInfoTextBox(txtManagementUrl);
		ConfigureReadOnlyInfoTextBox(txtProbeUrl);
		ConfigureReadOnlyInfoTextBox(txtConfigDirectory);
		ConfigureReadOnlyInfoTextBox(txtLogDirectory);
		ConfigureReadOnlyInfoTextBox(txtAuthDirectory);
		txtManagementUrl.Dock = DockStyle.Fill;
		txtProbeUrl.Dock = DockStyle.Fill;
		txtConfigDirectory.Dock = DockStyle.Fill;
		txtLogDirectory.Dock = DockStyle.Fill;
		txtAuthDirectory.Dock = DockStyle.Fill;
		AddInfoRow(tableLayoutPanel, 0, "管理页地址", txtManagementUrl);
		AddInfoRow(tableLayoutPanel, 1, "探测地址", txtProbeUrl);
		AddInfoRow(tableLayoutPanel, 2, "配置目录", txtConfigDirectory);
		AddInfoRow(tableLayoutPanel, 3, "日志目录", txtLogDirectory);
		AddInfoRow(tableLayoutPanel, 4, "鉴权目录", txtAuthDirectory);
		groupBox.Controls.Add(tableLayoutPanel);
		return groupBox;
	}

	private GroupBox BuildOptionsGroup()
	{
		GroupBox groupBox = CreateGroupBox("启动器选项");
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(2, 4, 2, 2),
			AutoScroll = false,
			ColumnCount = 1,
			RowCount = 6
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		for (int i = 0; i < 6; i++)
		{
			tableLayoutPanel.RowStyles.Add(new RowStyle());
		}
		ConfigureCheckBox(chkMinimizeToTray, "关闭窗口时最小化到托盘");
		ConfigureCheckBox(chkAutoStartService, "启动器打开时自动拉起服务");
		ConfigureCheckBox(chkLaunchLauncherOnWindowsStartup, "Windows 登录后自动启动此启动器");
		ConfigureCheckBox(chkOpenManagementAfterStart, "启动成功后自动打开管理页");
		chkMinimizeToTray.Margin = new Padding(3, 0, 3, 6);
		chkAutoStartService.Margin = new Padding(3, 0, 3, 6);
		chkLaunchLauncherOnWindowsStartup.Margin = new Padding(3, 0, 3, 6);
		chkOpenManagementAfterStart.Margin = new Padding(3, 0, 3, 6);
		nudAutoStartDelaySeconds.Minimum = 0m;
		nudAutoStartDelaySeconds.Maximum = 600m;
		nudAutoStartDelaySeconds.Width = 80;
		nudAutoStartDelaySeconds.TextAlign = HorizontalAlignment.Right;
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			AutoSize = true,
			ColumnCount = 3,
			Margin = new Padding(3, 0, 3, 8)
		};
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle());
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle());
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle());
		tableLayoutPanel2.Controls.Add(new Label
		{
			AutoSize = true,
			Font = new Font("Segoe UI", 10f),
			Text = "自动拉起服务前延迟",
			Margin = new Padding(0, 5, 6, 0)
		}, 0, 0);
		tableLayoutPanel2.Controls.Add(nudAutoStartDelaySeconds, 1, 0);
		tableLayoutPanel2.Controls.Add(new Label
		{
			AutoSize = true,
			Font = new Font("Segoe UI", 10f),
			Text = "秒",
			Margin = new Padding(6, 5, 0, 0)
		}, 2, 0);
		lblSettingsNote.AutoSize = true;
		lblSettingsNote.MaximumSize = new Size(760, 0);
		lblSettingsNote.Font = new Font("Segoe UI", 9f);
		lblSettingsNote.ForeColor = SystemColors.GrayText;
		lblSettingsNote.Margin = new Padding(3, 6, 3, 0);
		tableLayoutPanel.Controls.Add(chkMinimizeToTray, 0, 0);
		tableLayoutPanel.Controls.Add(chkAutoStartService, 0, 1);
		tableLayoutPanel.Controls.Add(chkLaunchLauncherOnWindowsStartup, 0, 2);
		tableLayoutPanel.Controls.Add(chkOpenManagementAfterStart, 0, 3);
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 4);
		tableLayoutPanel.Controls.Add(lblSettingsNote, 0, 5);
		groupBox.Controls.Add(tableLayoutPanel);
		return groupBox;
	}

	private GroupBox BuildDiagnosticsGroup()
	{
		GroupBox groupBox = CreateGroupBox("诊断输出");
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 2,
			Padding = new Padding(0, 2, 10, 0)
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 1,
			Margin = new Padding(0)
		};
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle());
		Label control = new Label
		{
			Dock = DockStyle.Fill,
			AutoEllipsis = true,
			Font = new Font("Segoe UI", 9f),
			ForeColor = SystemColors.GrayText,
			Text = "这里会显示启动器自身消息，以及 CPA 标准输出 / 错误输出。",
			TextAlign = ContentAlignment.MiddleLeft,
			Margin = new Padding(0, 8, 12, 0)
		};
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = false,
			Dock = DockStyle.Fill,
			Margin = new Padding(0, 4, 0, 0)
		};
		btnCopyDiagnostics.Text = "复制输出";
		btnCopyDiagnostics.Size = new Size(104, 30);
		btnCopyDiagnostics.Margin = new Padding(0, 0, 8, 0);
		btnExportDiagnostics.Text = "导出输出";
		btnExportDiagnostics.Size = new Size(104, 30);
		btnExportDiagnostics.Margin = new Padding(0, 0, 8, 0);
		btnClearDiagnostics.Text = "清空输出";
		btnClearDiagnostics.Size = new Size(104, 30);
		btnClearDiagnostics.Margin = new Padding(0);
		flowLayoutPanel.Controls.Add(btnCopyDiagnostics);
		flowLayoutPanel.Controls.Add(btnExportDiagnostics);
		flowLayoutPanel.Controls.Add(btnClearDiagnostics);
		txtDiagnostics.Dock = DockStyle.Fill;
		txtDiagnostics.Font = new Font("Consolas", 9f);
		txtDiagnostics.Multiline = true;
		txtDiagnostics.ReadOnly = true;
		txtDiagnostics.ScrollBars = ScrollBars.Vertical;
		txtDiagnostics.Margin = new Padding(0, 4, 0, 0);
		tableLayoutPanel2.Controls.Add(control, 0, 0);
		tableLayoutPanel2.Controls.Add(flowLayoutPanel, 1, 0);
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 0);
		tableLayoutPanel.Controls.Add(txtDiagnostics, 0, 1);
		groupBox.Controls.Add(tableLayoutPanel);
		return groupBox;
	}

	private void InitializeFormState()
	{
		UpdateSettingsNote();
	}

	private void WireEvents()
	{
		base.Load += MainForm_Load;
		base.Shown += MainForm_Shown;
		base.Resize += MainForm_Resize;
		base.FormClosing += MainForm_FormClosing;
		refreshTimer.Tick += async delegate
		{
			await RefreshUiAsync(recalculateRuntime: false);
		};
		btnStart.Click += async delegate
		{
			await StartServiceAsync();
		};
		btnStop.Click += async delegate
		{
			await StopServiceAsync();
		};
		btnOpenManagement.Click += delegate
		{
			OpenManagementPage();
		};
		btnOpenLogsDir.Click += delegate
		{
			OpenLogDirectory();
		};
		btnRefresh.Click += async delegate
		{
			await RefreshUiAsync(recalculateRuntime: true);
		};
		btnConfigureWizard.Click += async delegate
		{
			await OpenSetupWizardAsync(firstRun: false);
		};
		btnBrowseExecutable.Click += delegate
		{
			BrowseExecutable();
		};
		btnBrowseConfig.Click += delegate
		{
			BrowseConfig();
		};
		btnOpenExecutableDir.Click += delegate
		{
			OpenExecutableDirectory();
		};
		btnOpenConfigDir.Click += delegate
		{
			OpenConfigDirectory();
		};
		btnCopyDiagnostics.Click += delegate
		{
			CopyDiagnosticsToClipboard();
		};
		btnExportDiagnostics.Click += delegate
		{
			ExportDiagnosticsToFile();
		};
		btnClearDiagnostics.Click += delegate
		{
			ClearDiagnostics();
		};
		chkMinimizeToTray.CheckedChanged += delegate
		{
			SaveOptionsOnly();
		};
		chkAutoStartService.CheckedChanged += delegate
		{
			SaveOptionsOnly();
		};
		chkLaunchLauncherOnWindowsStartup.CheckedChanged += delegate
		{
			SaveOptionsOnly();
		};
		chkOpenManagementAfterStart.CheckedChanged += delegate
		{
			SaveOptionsOnly();
		};
		nudAutoStartDelaySeconds.ValueChanged += delegate
		{
			SaveOptionsOnly();
		};
		trayShowMenuItem.Click += delegate
		{
			ShowMainWindow();
		};
		trayStartMenuItem.Click += async delegate
		{
			await StartServiceAsync();
		};
		trayStopMenuItem.Click += async delegate
		{
			await StopServiceAsync();
		};
		trayOpenManagementMenuItem.Click += delegate
		{
			OpenManagementPage();
		};
		trayExitMenuItem.Click += delegate
		{
			ExitLauncher();
		};
		trayIcon.DoubleClick += delegate
		{
			ShowMainWindow();
		};
		processManager.OutputReceived += delegate(object? _, string line)
		{
			SafeUi(delegate
			{
				AppendDiagnosticLine(line);
			});
		};
		processManager.ProcessExited += delegate(object? _, int exitCode)
		{
			if (currentStatus != LauncherStatus.Stopping)
			{
				currentStatus = LauncherStatus.StartFailed;
				lastFailureMessage = $"CPA 进程已退出，退出码 {exitCode}。请查看下方诊断输出。";
			}
			else
			{
				currentStatus = LauncherStatus.Stopped;
			}
			SafeUi(delegate
			{
				_ = RefreshUiAsync(recalculateRuntime: false);
			});
		};
	}

	private async void MainForm_Load(object? sender, EventArgs e)
	{
		try
		{
			settings = settingsStore.Load();
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			settings = new LauncherSettings();
			AppendLauncherLog("读取启动器设置失败：" + ex2.Message);
		}
		settings.LaunchLauncherOnWindowsStartup = startupManager.IsEnabled();
		ApplySettingsToUi();
		RecalculateRuntimeInfo();
		await RefreshUiAsync(recalculateRuntime: false);
		refreshTimer.Start();
	}

	private async void MainForm_Shown(object? sender, EventArgs e)
	{
		if (!IsConfigurationReady() && !initialPromptShown)
		{
			initialPromptShown = true;
			await OpenSetupWizardAsync(firstRun: true);
		}
		if (settings.AutoStartService && IsConfigurationReady())
		{
			int delaySeconds = Math.Max(0, settings.AutoStartDelaySeconds);
			if (delaySeconds > 0)
			{
				AppendLauncherLog($"已启用自动拉起延迟，{delaySeconds} 秒后再尝试启动 CPA。");
				await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
				if (base.IsDisposed || isExiting)
				{
					return;
				}
			}
			await StartServiceAsync();
		}
		if (startMinimizedToTray && IsConfigurationReady())
		{
			HideToTray();
		}
	}

	private void MainForm_Resize(object? sender, EventArgs e)
	{
		if (base.WindowState == FormWindowState.Minimized)
		{
			HideToTray();
		}
	}

	private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
	{
		if (!isExiting && e.CloseReason == CloseReason.UserClosing && chkMinimizeToTray.Checked)
		{
			e.Cancel = true;
			HideToTray();
			return;
		}
		if (processManager.IsManagedProcessRunning)
		{
			switch (MessageBox.Show(this, "当前 CPA 仍由启动器托管。\n\n选择“是”会先停止服务再退出；选择“否”会保留服务继续运行，仅退出启动器；选择“取消”则返回。", "退出前确认", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
			{
			case DialogResult.Cancel:
				e.Cancel = true;
				isExiting = false;
				return;
			case DialogResult.Yes:
			{
				ProcessCommandResult result = processManager.StopAsync().GetAwaiter().GetResult();
				AppendLauncherLog(result.Message);
				break;
			}
			}
		}
		refreshTimer.Stop();
		trayIcon.Visible = false;
	}

	private async Task StartServiceAsync()
	{
		if (!EnsureConfigurationReady())
		{
			return;
		}
		if (runtimeInfo == null)
		{
			RecalculateRuntimeInfo();
			if (runtimeInfo == null)
			{
				return;
			}
		}
		if (processManager.IsManagedProcessRunning)
		{
			AppendLauncherLog("CPA 已经由当前启动器托管，无需重复启动。");
			await RefreshUiAsync(recalculateRuntime: false);
			return;
		}
		if (await ProbeServiceAsync(runtimeInfo.ProbeUrl))
		{
			DialogResult confirm = MessageBox.Show(this, "检测到当前管理页地址已经有响应。\n\n这通常表示端口上已经跑着一个 CPA 实例。继续启动可能会因为端口占用而失败。\n\n是否仍然继续？", "检测到已有服务", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);
			if (confirm != DialogResult.Yes)
			{
				return;
			}
		}
		currentStatus = LauncherStatus.Starting;
		lastFailureMessage = null;
		pendingOpenManagementAfterStart = settings.OpenManagementPageAfterStart && !startMinimizedToTray;
		AppendLauncherLog("准备启动 CPA：" + runtimeInfo.ExecutablePath + " --config " + runtimeInfo.ConfigPath);
		UpdateStatusVisual(serviceReachable: false);
		ProcessCommandResult result = await processManager.StartAsync(runtimeInfo.ExecutablePath, runtimeInfo.ConfigPath);
		AppendLauncherLog(result.Message);
		if (!result.Success)
		{
			currentStatus = LauncherStatus.StartFailed;
			lastFailureMessage = result.Message;
			pendingOpenManagementAfterStart = false;
			await RefreshUiAsync(recalculateRuntime: false);
			return;
		}
		if (await WaitForServiceStateAsync(runtimeInfo.ProbeUrl, expectedState: true, TimeSpan.FromSeconds(10.0)))
		{
			currentStatus = LauncherStatus.Running;
			lastFailureMessage = null;
			AppendLauncherLog("已检测到管理页地址响应，CPA 启动完成。");
		}
		else if (!processManager.IsManagedProcessRunning)
		{
			currentStatus = LauncherStatus.StartFailed;
			if (lastFailureMessage == null)
			{
				lastFailureMessage = "CPA 进程启动后很快退出，服务没有成功就绪。";
			}
		}
		else
		{
			currentStatus = LauncherStatus.Starting;
			AppendLauncherLog("CPA 进程已拉起，但管理页暂时还未响应。你可以稍后再点“重新检测”。");
		}
		await RefreshUiAsync(recalculateRuntime: false);
	}

	private async Task StopServiceAsync()
	{
		if (!processManager.IsManagedProcessRunning)
		{
			bool flag = runtimeInfo != null;
			bool flag2 = flag;
			if (flag2)
			{
				flag2 = await ProbeServiceAsync(runtimeInfo.ProbeUrl);
			}
			if (flag2)
			{
				MessageBox.Show(this, "当前端口上确实有 CPA 响应，但不是当前启动器托管起来的进程。\n\n为了避免误杀别的实例，启动器不会直接停止它。", "无法停止外部实例", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			}
			else
			{
				AppendLauncherLog("当前没有可停止的托管进程。");
			}
			await RefreshUiAsync(recalculateRuntime: false);
			return;
		}
		currentStatus = LauncherStatus.Stopping;
		UpdateStatusVisual(await ProbeRuntimeAsync());
		ProcessCommandResult result = await processManager.StopAsync();
		AppendLauncherLog(result.Message);
		if (!result.Success)
		{
			currentStatus = LauncherStatus.StartFailed;
			lastFailureMessage = result.Message;
			await RefreshUiAsync(recalculateRuntime: false);
			return;
		}
		if (runtimeInfo != null)
		{
			await WaitForServiceStateAsync(runtimeInfo.ProbeUrl, expectedState: false, TimeSpan.FromSeconds(5.0));
		}
		currentStatus = LauncherStatus.Stopped;
		lastFailureMessage = null;
		pendingOpenManagementAfterStart = false;
		await RefreshUiAsync(recalculateRuntime: false);
	}

	private async Task OpenSetupWizardAsync(bool firstRun)
	{
		using SetupWizardForm wizard = new SetupWizardForm(settings, settingsStore.SettingsFilePath);
		DialogResult result = wizard.ShowDialog(this);
		if (result != DialogResult.OK || wizard.ResultSettings == null)
		{
			if (firstRun && !IsConfigurationReady())
			{
				AppendLauncherLog("首次配置已取消，你可以稍后点击“配置向导”继续。");
			}
			return;
		}
		settings = wizard.ResultSettings;
		ApplySettingsToUi();
		SaveSettings();
		RecalculateRuntimeInfo();
		AppendLauncherLog(firstRun ? "首次配置已完成。" : "已更新启动器配置。");
		await RefreshUiAsync(recalculateRuntime: true);
	}

	private void BrowseExecutable()
	{
		using OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "选择 cli-proxy-api.exe",
			Filter = "CLIProxyAPI 可执行文件|cli-proxy-api.exe|Windows 可执行文件|*.exe|所有文件|*.*",
			CheckFileExists = true,
			CheckPathExists = true
		};
		if (openFileDialog.ShowDialog(this) == DialogResult.OK)
		{
			txtExecutablePath.Text = openFileDialog.FileName;
			AutoDiscoverConfigForExecutable(openFileDialog.FileName);
			UpdateSettingsFromUi();
			SaveSettings();
			RecalculateRuntimeInfo();
			RefreshUiAsync(recalculateRuntime: false);
		}
	}

	private void BrowseConfig()
	{
		using OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "选择 config.yaml",
			Filter = "YAML 配置文件|*.yaml;*.yml|所有文件|*.*",
			CheckFileExists = true,
			CheckPathExists = true
		};
		if (openFileDialog.ShowDialog(this) == DialogResult.OK)
		{
			txtConfigPath.Text = openFileDialog.FileName;
			UpdateSettingsFromUi();
			SaveSettings();
			RecalculateRuntimeInfo();
			RefreshUiAsync(recalculateRuntime: false);
		}
	}

	private void AutoDiscoverConfigForExecutable(string executablePath)
	{
		string directoryName = Path.GetDirectoryName(executablePath);
		if (!string.IsNullOrWhiteSpace(directoryName))
		{
			string text = Path.Combine(directoryName, "config.yaml");
			if (!File.Exists(text))
			{
				AppendLauncherLog("已选择 exe，但同目录下没有发现 config.yaml，请手动指定配置文件。");
				return;
			}
			txtConfigPath.Text = text;
			AppendLauncherLog("已自动识别同目录配置文件：" + text);
		}
	}

	private void OpenManagementPage()
	{
		if (runtimeInfo == null)
		{
			MessageBox.Show(this, "当前还没有可用的管理页地址，请先配置 exe 与 config。", "无法打开管理页", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		if (runtimeInfo.ControlPanelDisabled)
		{
			DialogResult dialogResult = MessageBox.Show(this, "当前配置显示 remote-management.disable-control-panel = true。\n\n继续打开管理页时，浏览器里可能会看到 404。是否继续？", "控制面板可能已禁用", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);
			if (dialogResult != DialogResult.Yes)
			{
				return;
			}
		}
		OpenWithShell(runtimeInfo.ManagementUrl);
	}

	private void OpenLogDirectory()
	{
		if (runtimeInfo == null)
		{
			MessageBox.Show(this, "当前还没有可推导的日志目录。", "无法打开日志目录", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		if (!runtimeInfo.LoggingToFile)
		{
			DialogResult dialogResult = MessageBox.Show(this, "当前配置里 logging-to-file = false。\n\n日志目录依然可以打开，但此时 CPA 默认主要输出到标准输出。是否继续打开推导出的目录？", "日志文件可能尚未启用", MessageBoxButtons.YesNo, MessageBoxIcon.Asterisk);
			if (dialogResult != DialogResult.Yes)
			{
				return;
			}
		}
		OpenDirectory(runtimeInfo.LogDirectory);
	}

	private void OpenExecutableDirectory()
	{
		if (!HasExistingFile(txtExecutablePath.Text))
		{
			MessageBox.Show(this, "还没有有效的 exe 路径。", "无法打开目录", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}
		else
		{
			OpenPathInExplorer(txtExecutablePath.Text, selectFile: true);
		}
	}

	private void OpenConfigDirectory()
	{
		if (!HasExistingFile(txtConfigPath.Text))
		{
			MessageBox.Show(this, "还没有有效的 config.yaml 路径。", "无法打开目录", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}
		else
		{
			OpenPathInExplorer(txtConfigPath.Text, selectFile: true);
		}
	}

	private void CopyDiagnosticsToClipboard()
	{
		string text = BuildDiagnosticsExportText();
		if (string.IsNullOrWhiteSpace(text))
		{
			MessageBox.Show(this, "当前没有可复制的诊断内容。", "没有可复制内容", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		Clipboard.SetText(text);
		AppendLauncherLog("已将诊断输出复制到剪贴板。");
	}

	private void ExportDiagnosticsToFile()
	{
		using SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Title = "导出诊断输出",
			Filter = "文本文件|*.txt|所有文件|*.*",
			FileName = $"CPALauncher-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
			OverwritePrompt = true
		};
		if (saveFileDialog.ShowDialog(this) == DialogResult.OK)
		{
			File.WriteAllText(saveFileDialog.FileName, BuildDiagnosticsExportText());
			AppendLauncherLog("已导出诊断输出：" + saveFileDialog.FileName);
		}
	}

	private string BuildDiagnosticsExportText()
	{
		List<string> list = new List<string>
		{
			"CPA Launcher Diagnostics",
			$"GeneratedAt: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
			"LauncherSettingsPath: " + settingsStore.SettingsFilePath,
			"ExecutablePath: " + (settings.ExecutablePath ?? string.Empty),
			"ConfigPath: " + (settings.ConfigPath ?? string.Empty),
			$"AutoStartService: {settings.AutoStartService}",
			$"LaunchLauncherOnWindowsStartup: {settings.LaunchLauncherOnWindowsStartup}",
			$"AutoStartDelaySeconds: {settings.AutoStartDelaySeconds}",
			$"OpenManagementPageAfterStart: {settings.OpenManagementPageAfterStart}",
			$"MinimizeToTrayOnClose: {settings.MinimizeToTrayOnClose}"
		};
		if (runtimeInfo != null)
		{
			list.Add("ManagementUrl: " + runtimeInfo.ManagementUrl);
			list.Add("ProbeUrl: " + runtimeInfo.ProbeUrl);
			list.Add("LogDirectory: " + runtimeInfo.LogDirectory);
			list.Add("AuthDirectory: " + (runtimeInfo.AuthDirectory ?? string.Empty));
		}
		list.Add(string.Empty);
		list.Add("---- Diagnostics ----");
		list.Add(txtDiagnostics.Text);
		return string.Join(Environment.NewLine, list);
	}

	private void ClearDiagnostics()
	{
		processManager.ClearRecentOutput();
		txtDiagnostics.Clear();
		AppendLauncherLog("已清空诊断输出。");
	}

	private void SaveOptionsOnly()
	{
		if (!suppressSettingSaves)
		{
			UpdateSettingsFromUi();
			SaveSettings();
		}
	}

	private async Task RefreshUiAsync(bool recalculateRuntime)
	{
		if (isRefreshing)
		{
			return;
		}
		isRefreshing = true;
		try
		{
			if (recalculateRuntime)
			{
				RecalculateRuntimeInfo();
			}
			UpdateInferredInfo();
			bool serviceReachable = await ProbeRuntimeAsync();
			UpdateStatusVisual(serviceReachable);
			if (pendingOpenManagementAfterStart && serviceReachable && runtimeInfo != null)
			{
				pendingOpenManagementAfterStart = false;
				OpenWithShell(runtimeInfo.ManagementUrl);
			}
		}
		finally
		{
			isRefreshing = false;
		}
	}

	private void UpdateStatusVisual(bool serviceReachable)
	{
		if (!IsConfigurationReady())
		{
			SetStatusVisual(LauncherStatus.Unconfigured, "未配置", "请先选择 cli-proxy-api.exe 与 config.yaml。", Color.FromArgb(232, 236, 241), Color.FromArgb(32, 31, 30));
		}
		else if (processManager.IsManagedProcessRunning)
		{
			if (serviceReachable)
			{
				lastFailureMessage = null;
				SetStatusVisual(LauncherStatus.Running, "运行中", "CPA 已运行，当前由启动器托管。", Color.FromArgb(209, 241, 224), Color.FromArgb(15, 85, 43));
			}
			else
			{
				SetStatusVisual(LauncherStatus.Starting, "启动中", "进程已拉起，正在等待管理页响应。", Color.FromArgb(255, 242, 204), Color.FromArgb(120, 79, 0));
			}
		}
		else if (serviceReachable)
		{
			SetStatusVisual(LauncherStatus.Running, "外部运行", "检测到目标地址已有 CPA 响应，但不是当前启动器托管的进程。", Color.FromArgb(222, 236, 255), Color.FromArgb(0, 74, 173));
		}
		else if (currentStatus == LauncherStatus.StartFailed && !string.IsNullOrWhiteSpace(lastFailureMessage))
		{
			SetStatusVisual(LauncherStatus.StartFailed, "启动失败", lastFailureMessage, Color.FromArgb(255, 226, 226), Color.FromArgb(163, 0, 0));
		}
		else if (currentStatus == LauncherStatus.Stopping)
		{
			SetStatusVisual(LauncherStatus.Stopping, "停止中", "正在尝试结束当前托管的 CPA 进程。", Color.FromArgb(255, 242, 204), Color.FromArgb(120, 79, 0));
		}
		else
		{
			SetStatusVisual(LauncherStatus.Stopped, "已停止", "当前没有运行中的托管进程。", Color.FromArgb(238, 238, 238), Color.FromArgb(66, 66, 66));
		}
		lblManagedPidValue.Text = processManager.ManagedProcessId?.ToString() ?? (serviceReachable ? "外部实例" : "无");
		btnStart.Enabled = IsConfigurationReady() && !processManager.IsManagedProcessRunning;
		btnStop.Enabled = processManager.IsManagedProcessRunning;
		btnOpenManagement.Enabled = runtimeInfo != null;
		btnOpenLogsDir.Enabled = runtimeInfo != null;
		btnOpenExecutableDir.Enabled = HasExistingFile(txtExecutablePath.Text);
		btnOpenConfigDir.Enabled = HasExistingFile(txtConfigPath.Text);
		trayStartMenuItem.Enabled = btnStart.Enabled;
		trayStopMenuItem.Enabled = btnStop.Enabled;
		trayOpenManagementMenuItem.Enabled = btnOpenManagement.Enabled;
	}

	private void SetStatusVisual(LauncherStatus status, string badgeText, string detail, Color backgroundColor, Color foregroundColor)
	{
		currentStatus = status;
		lblStatusBadge.Text = badgeText;
		lblStatusBadge.BackColor = backgroundColor;
		lblStatusBadge.ForeColor = foregroundColor;
		lblStatusDetail.Text = detail;
		lblStatusDetail.ForeColor = foregroundColor;
		trayIcon.Text = "CPA Launcher - " + badgeText;
	}

	private void UpdateInferredInfo()
	{
		if (runtimeInfo == null)
		{
			txtManagementUrl.Clear();
			txtProbeUrl.Clear();
			txtConfigDirectory.Clear();
			txtLogDirectory.Clear();
			txtAuthDirectory.Clear();
		}
		else
		{
			txtManagementUrl.Text = runtimeInfo.ManagementUrl;
			txtProbeUrl.Text = runtimeInfo.ProbeUrl;
			txtConfigDirectory.Text = runtimeInfo.ConfigDirectory;
			txtLogDirectory.Text = (runtimeInfo.LoggingToFile ? runtimeInfo.LogDirectory : (runtimeInfo.LogDirectory + "（当前 logging-to-file = false）"));
			txtAuthDirectory.Text = (string.IsNullOrWhiteSpace(runtimeInfo.AuthDirectory) ? "未配置 auth-dir" : runtimeInfo.AuthDirectory);
		}
	}

	private void RecalculateRuntimeInfo()
	{
		runtimeInfo = null;
		UpdateSettingsFromUi();
		if (!IsConfigurationReady())
		{
			return;
		}
		try
		{
			runtimeInfo = configInspector.Inspect(settings.ExecutablePath, settings.ConfigPath);
		}
		catch (Exception ex)
		{
			runtimeInfo = null;
			lastFailureMessage = "配置解析失败：" + ex.Message;
			AppendLauncherLog(lastFailureMessage);
		}
	}

	private async Task<bool> ProbeRuntimeAsync()
	{
		bool flag = runtimeInfo != null;
		bool flag2 = flag;
		if (flag2)
		{
			flag2 = await ProbeServiceAsync(runtimeInfo.ProbeUrl);
		}
		return flag2;
	}

	private async Task<bool> ProbeServiceAsync(string probeUrl)
	{
		try
		{
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, probeUrl);
			using (await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
			{
				return true;
			}
		}
		catch
		{
			return false;
		}
	}

	private async Task<bool> WaitForServiceStateAsync(string probeUrl, bool expectedState, TimeSpan timeout)
	{
		DateTime deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline)
		{
			if (await ProbeServiceAsync(probeUrl) == expectedState)
			{
				return true;
			}
			await Task.Delay(500);
		}
		return false;
	}

	private bool EnsureConfigurationReady()
	{
		UpdateSettingsFromUi();
		if (HasExistingFile(settings.ExecutablePath) && HasExistingFile(settings.ConfigPath))
		{
			return true;
		}
		MessageBox.Show(this, "请先配置有效的 cli-proxy-api.exe 与 config.yaml 路径。", "路径尚未完成", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		return false;
	}

	private bool IsConfigurationReady()
	{
		return HasExistingFile(settings.ExecutablePath) && HasExistingFile(settings.ConfigPath);
	}

	private static bool HasExistingFile(string? path)
	{
		return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
	}

	private void ApplySettingsToUi()
	{
		suppressSettingSaves = true;
		try
		{
			txtExecutablePath.Text = settings.ExecutablePath ?? string.Empty;
			txtConfigPath.Text = settings.ConfigPath ?? string.Empty;
			chkMinimizeToTray.Checked = settings.MinimizeToTrayOnClose;
			chkAutoStartService.Checked = settings.AutoStartService;
			chkLaunchLauncherOnWindowsStartup.Checked = settings.LaunchLauncherOnWindowsStartup;
			chkOpenManagementAfterStart.Checked = settings.OpenManagementPageAfterStart;
			nudAutoStartDelaySeconds.Value = Math.Max(nudAutoStartDelaySeconds.Minimum, Math.Min(nudAutoStartDelaySeconds.Maximum, settings.AutoStartDelaySeconds));
		}
		finally
		{
			suppressSettingSaves = false;
		}
	}

	private void UpdateSettingsFromUi()
	{
		settings.ExecutablePath = NormalizePathOrNull(txtExecutablePath.Text);
		settings.ConfigPath = NormalizePathOrNull(txtConfigPath.Text);
		settings.MinimizeToTrayOnClose = chkMinimizeToTray.Checked;
		settings.AutoStartService = chkAutoStartService.Checked;
		settings.LaunchLauncherOnWindowsStartup = chkLaunchLauncherOnWindowsStartup.Checked;
		settings.AutoStartDelaySeconds = decimal.ToInt32(nudAutoStartDelaySeconds.Value);
		settings.OpenManagementPageAfterStart = chkOpenManagementAfterStart.Checked;
	}

	private void SaveSettings()
	{
		try
		{
			startupManager.SetEnabled(settings.LaunchLauncherOnWindowsStartup);
			settingsStore.Save(settings);
			UpdateSettingsNote();
		}
		catch (Exception ex)
		{
			settings.LaunchLauncherOnWindowsStartup = startupManager.IsEnabled();
			try
			{
				settingsStore.Save(settings);
			}
			catch
			{
			}
			ApplySettingsToUi();
			UpdateSettingsNote();
			MessageBox.Show(this, "保存启动器设置失败：" + ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private static string? NormalizePathOrNull(string? path)
	{
		return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path.Trim());
	}

	private void ShowMainWindow()
	{
		Show();
		base.ShowInTaskbar = true;
		base.WindowState = FormWindowState.Normal;
		Activate();
		BringToFront();
	}

	private void HideToTray()
	{
		Hide();
		base.ShowInTaskbar = false;
		trayIcon.Visible = true;
		if (!trayHintShown && !startMinimizedToTray)
		{
			trayIcon.ShowBalloonTip(2000, "CPA Launcher", "窗口已最小化到系统托盘，双击托盘图标可恢复。", ToolTipIcon.Info);
			trayHintShown = true;
		}
	}

	private void ExitLauncher()
	{
		isExiting = true;
		Close();
	}

	private static void OpenWithShell(string target)
	{
		Process.Start(new ProcessStartInfo
		{
			FileName = target,
			UseShellExecute = true
		});
	}

	private static void OpenDirectory(string directoryPath)
	{
		if (Directory.Exists(directoryPath))
		{
			OpenWithShell(directoryPath);
			return;
		}
		string text = directoryPath;
		while (!string.IsNullOrWhiteSpace(text) && !Directory.Exists(text))
		{
			text = Path.GetDirectoryName(text);
		}
		if (!string.IsNullOrWhiteSpace(text) && Directory.Exists(text))
		{
			OpenWithShell(text);
		}
		else
		{
			MessageBox.Show("找不到可打开的目录：" + directoryPath, "打开目录失败", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}
	}

	private static void OpenPathInExplorer(string path, bool selectFile)
	{
		if (selectFile)
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = "explorer.exe",
				Arguments = "/select,\"" + path + "\"",
				UseShellExecute = true
			});
		}
		else
		{
			OpenWithShell(path);
		}
	}

	private void AppendLauncherLog(string message)
	{
		AppendDiagnosticLine($"[{DateTime.Now:HH:mm:ss}] launcher | {message}");
	}

	private void AppendDiagnosticLine(string line)
	{
		if (!string.IsNullOrWhiteSpace(line) && !txtDiagnostics.IsDisposed)
		{
			if (txtDiagnostics.TextLength > 120000)
			{
				txtDiagnostics.Lines = txtDiagnostics.Lines.Skip(Math.Max(0, txtDiagnostics.Lines.Length - 220)).ToArray();
			}
			if (txtDiagnostics.TextLength > 0)
			{
				txtDiagnostics.AppendText(Environment.NewLine);
			}
			txtDiagnostics.AppendText(line);
			txtDiagnostics.SelectionStart = txtDiagnostics.TextLength;
			txtDiagnostics.ScrollToCaret();
		}
	}

	private void SafeUi(Action action)
	{
		if (!base.IsDisposed)
		{
			if (base.InvokeRequired)
			{
				BeginInvoke(action);
			}
			else
			{
				action();
			}
		}
	}

	private void UpdateSettingsNote()
	{
		string value = (settings.LaunchLauncherOnWindowsStartup ? "已启用（登录后将以托盘方式启动）" : "未启用");
		int value2 = Math.Max(0, settings.AutoStartDelaySeconds);
		lblSettingsNote.Text = $"启动器设置保存在：{settingsStore.SettingsFilePath}\nWindows 开机自启：{value}\n自动拉起延迟：{value2} 秒";
	}

	private static GroupBox CreateGroupBox(string text)
	{
		return new GroupBox
		{
			Dock = DockStyle.Fill,
			Text = text,
			Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
			Padding = new Padding(14, 12, 14, 14)
		};
	}

	private static void ConfigureActionButton(Button button, string text, int width = 138)
	{
		button.Text = text;
		button.Size = new Size(width, 40);
		button.Margin = new Padding(3);
		button.Font = new Font("Segoe UI", 10f);
	}

	private static void ConfigureMiniButton(Button button, string text, int width = 112)
	{
		button.Text = text;
		button.Size = new Size(width, 31);
		button.Anchor = AnchorStyles.Left | AnchorStyles.Right;
		button.Font = new Font("Segoe UI", 10f);
	}

	private static Label CreateRowLabel(string text)
	{
		return new Label
		{
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			Font = new Font("Segoe UI", 10f),
			Text = text
		};
	}

	private static void ConfigureReadOnlyPathTextBox(TextBox textBox)
	{
		textBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
		textBox.ReadOnly = true;
		textBox.Font = new Font("Segoe UI", 10f);
	}

	private static void ConfigureReadOnlyInfoTextBox(TextBox textBox)
	{
		textBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
		textBox.ReadOnly = true;
		textBox.Font = new Font("Segoe UI", 10f);
	}

	private static void AddInfoRow(TableLayoutPanel layout, int rowIndex, string labelText, TextBox textBox)
	{
		layout.Controls.Add(CreateRowLabel(labelText), 0, rowIndex);
		layout.Controls.Add(textBox, 1, rowIndex);
	}

	private static void ConfigureCheckBox(CheckBox checkBox, string text)
	{
		checkBox.AutoSize = true;
		checkBox.Font = new Font("Segoe UI", 10f);
		checkBox.Text = text;
	}
}
