using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using CPALauncher.Models;

namespace CPALauncher;

public sealed class SetupWizardForm : Form
{
	private readonly TextBox txtExecutablePath = new TextBox();

	private readonly TextBox txtConfigPath = new TextBox();

	private readonly CheckBox chkMinimizeToTray = new CheckBox();

	private readonly CheckBox chkAutoStartService = new CheckBox();

	private readonly CheckBox chkLaunchLauncherOnWindowsStartup = new CheckBox();

	private readonly CheckBox chkOpenManagementAfterStart = new CheckBox();

	private readonly NumericUpDown nudAutoStartDelaySeconds = new NumericUpDown();

	private readonly Button btnSave = new Button();

	private readonly Button btnCancel = new Button();

	public LauncherSettings? ResultSettings { get; private set; }

	public SetupWizardForm(LauncherSettings? initialSettings, string settingsFilePath)
	{
		InitializeUi(settingsFilePath);
		LoadInitialSettings(initialSettings ?? new LauncherSettings());
	}

	private void InitializeUi(string settingsFilePath)
	{
		SuspendLayout();
		Text = "CPA Launcher - 首次配置向导";
		base.StartPosition = FormStartPosition.CenterParent;
		base.FormBorderStyle = FormBorderStyle.Sizable;
		base.MaximizeBox = false;
		base.MinimizeBox = false;
		base.ShowInTaskbar = false;
		base.ClientSize = new Size(980, 760);
		MinimumSize = new Size(920, 700);
		Font = new Font("Segoe UI", 9f);
		base.AutoScaleMode = AutoScaleMode.Dpi;
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(18, 16, 18, 16),
			ColumnCount = 1,
			RowCount = 4
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 184f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 60f));
		base.Controls.Add(tableLayoutPanel);
		tableLayoutPanel.Controls.Add(BuildHeader(), 0, 0);
		tableLayoutPanel.Controls.Add(BuildPathGroup(), 0, 1);
		tableLayoutPanel.Controls.Add(BuildOptionGroup(settingsFilePath), 0, 2);
		tableLayoutPanel.Controls.Add(BuildFooter(), 0, 3);
		base.AcceptButton = btnSave;
		base.CancelButton = btnCancel;
		ResumeLayout(performLayout: false);
	}

	private Control BuildHeader()
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			ColumnCount = 1,
			RowCount = 4
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle());
		tableLayoutPanel.RowStyles.Add(new RowStyle());
		tableLayoutPanel.RowStyles.Add(new RowStyle());
		tableLayoutPanel.RowStyles.Add(new RowStyle());
		Label control = new Label
		{
			AutoSize = true,
			Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
			Text = "首次配置向导",
			Margin = new Padding(0)
		};
		Label control2 = new Label
		{
			AutoSize = true,
			MaximumSize = new Size(900, 0),
			Font = new Font("Segoe UI", 10f),
			ForeColor = SystemColors.GrayText,
			Text = "这个向导只负责告诉启动器：你的 cli-proxy-api.exe 在哪里、你的 config.yaml 在哪里，以及启动器自己的几个习惯选项。它不会改写现有 CPA 配置。",
			Margin = new Padding(2, 8, 0, 0)
		};
		LinkLabel linkLabel = new LinkLabel
		{
			AutoSize = true,
			Text = "打开官方帮助文档",
			Margin = new Padding(2, 10, 0, 0)
		};
		linkLabel.LinkClicked += delegate
		{
			OpenWithShell("https://help.router-for.me/cn/");
		};
		Label control3 = new Label
		{
			AutoSize = true,
			MaximumSize = new Size(900, 0),
			Font = new Font("Segoe UI", 9f),
			ForeColor = SystemColors.GrayText,
			Text = "常见建议：exe 与 config.yaml 最好放同目录；如果勾选开机自启，通常也建议同时勾选自动拉起服务。",
			Margin = new Padding(2, 8, 0, 0)
		};
		tableLayoutPanel.Controls.Add(control, 0, 0);
		tableLayoutPanel.Controls.Add(control2, 0, 1);
		tableLayoutPanel.Controls.Add(linkLabel, 0, 2);
		tableLayoutPanel.Controls.Add(control3, 0, 3);
		return tableLayoutPanel;
	}

	private Control BuildPathGroup()
	{
		GroupBox groupBox = CreateGroupBox("第 1 步：定位 exe 与配置文件");
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
		Button button = new Button
		{
			Text = "选择 exe",
			Size = new Size(120, 32),
			Margin = new Padding(0, 0, 8, 0)
		};
		Button button2 = new Button
		{
			Text = "同目录识别",
			Size = new Size(126, 32),
			Margin = new Padding(0)
		};
		Button button3 = new Button
		{
			Text = "选择配置",
			Size = new Size(120, 32),
			Margin = new Padding(0, 0, 8, 0)
		};
		Button button4 = new Button
		{
			Text = "清空",
			Size = new Size(126, 32),
			Margin = new Padding(0)
		};
		txtExecutablePath.ReadOnly = true;
		txtExecutablePath.Dock = DockStyle.Fill;
		txtExecutablePath.Font = new Font("Segoe UI", 10f);
		txtExecutablePath.Margin = new Padding(0, 6, 10, 6);
		txtConfigPath.ReadOnly = true;
		txtConfigPath.Dock = DockStyle.Fill;
		txtConfigPath.Font = new Font("Segoe UI", 10f);
		txtConfigPath.Margin = new Padding(0, 6, 10, 6);
		button.Click += delegate
		{
			BrowseExecutable();
		};
		button2.Click += delegate
		{
			TryAutoDiscoverConfig(txtExecutablePath.Text, showMessage: true);
		};
		button3.Click += delegate
		{
			BrowseConfig();
		};
		button4.Click += delegate
		{
			txtConfigPath.Clear();
		};
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
		flowLayoutPanel.Controls.Add(button);
		flowLayoutPanel.Controls.Add(button2);
		flowLayoutPanel2.Controls.Add(button3);
		flowLayoutPanel2.Controls.Add(button4);
		tableLayoutPanel.Controls.Add(CreateRowLabel("CPA exe"), 0, 0);
		tableLayoutPanel.Controls.Add(txtExecutablePath, 1, 0);
		tableLayoutPanel.Controls.Add(flowLayoutPanel, 2, 0);
		tableLayoutPanel.Controls.Add(CreateRowLabel("config.yaml"), 0, 1);
		tableLayoutPanel.Controls.Add(txtConfigPath, 1, 1);
		tableLayoutPanel.Controls.Add(flowLayoutPanel2, 2, 1);
		groupBox.Controls.Add(tableLayoutPanel);
		return groupBox;
	}

	private Control BuildOptionGroup(string settingsFilePath)
	{
		GroupBox groupBox = CreateGroupBox("第 2 步：启动器自身选项");
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 6,
			Padding = new Padding(0, 4, 10, 0),
			AutoScroll = false
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
		tableLayoutPanel.RowStyles.Add(new RowStyle());
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
		Label control = new Label
		{
			AutoSize = true,
			MaximumSize = new Size(900, 0),
			Font = new Font("Segoe UI", 9f),
			ForeColor = SystemColors.GrayText,
			Text = "建议：如果勾选开机自启，通常也建议同时勾选自动拉起服务。\n启动器自己的设置将保存到：" + settingsFilePath + "\n如果你以后换了 CPA 的目录或 exe，只需要重新打开这个向导即可。",
			Margin = new Padding(3, 6, 3, 0)
		};
		tableLayoutPanel.Controls.Add(chkMinimizeToTray, 0, 0);
		tableLayoutPanel.Controls.Add(chkAutoStartService, 0, 1);
		tableLayoutPanel.Controls.Add(chkLaunchLauncherOnWindowsStartup, 0, 2);
		tableLayoutPanel.Controls.Add(chkOpenManagementAfterStart, 0, 3);
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 4);
		tableLayoutPanel.Controls.Add(control, 0, 5);
		groupBox.Controls.Add(tableLayoutPanel);
		return groupBox;
	}

	private Control BuildFooter()
	{
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.RightToLeft,
			WrapContents = false,
			Padding = new Padding(0, 10, 10, 0)
		};
		btnSave.Text = "保存并完成";
		btnSave.Size = new Size(120, 34);
		btnSave.Margin = new Padding(8, 0, 0, 0);
		btnSave.Click += delegate
		{
			SaveAndClose();
		};
		btnCancel.Text = "取消";
		btnCancel.Size = new Size(96, 34);
		btnCancel.DialogResult = DialogResult.Cancel;
		btnCancel.Margin = new Padding(8, 0, 0, 0);
		flowLayoutPanel.Controls.Add(btnCancel);
		flowLayoutPanel.Controls.Add(btnSave);
		return flowLayoutPanel;
	}

	private void LoadInitialSettings(LauncherSettings settings)
	{
		txtExecutablePath.Text = settings.ExecutablePath ?? string.Empty;
		txtConfigPath.Text = settings.ConfigPath ?? string.Empty;
		chkMinimizeToTray.Checked = settings.MinimizeToTrayOnClose;
		chkAutoStartService.Checked = settings.AutoStartService;
		chkLaunchLauncherOnWindowsStartup.Checked = settings.LaunchLauncherOnWindowsStartup;
		chkOpenManagementAfterStart.Checked = settings.OpenManagementPageAfterStart;
		nudAutoStartDelaySeconds.Value = Math.Max(nudAutoStartDelaySeconds.Minimum, Math.Min(nudAutoStartDelaySeconds.Maximum, settings.AutoStartDelaySeconds));
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
			TryAutoDiscoverConfig(openFileDialog.FileName, showMessage: false);
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
		}
	}

	private void TryAutoDiscoverConfig(string executablePath, bool showMessage)
	{
		if (string.IsNullOrWhiteSpace(executablePath))
		{
			if (showMessage)
			{
				MessageBox.Show(this, "请先选择 cli-proxy-api.exe。", "还没有 exe 路径", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			}
			return;
		}
		string directoryName = Path.GetDirectoryName(executablePath);
		if (string.IsNullOrWhiteSpace(directoryName))
		{
			return;
		}
		string text = Path.Combine(directoryName, "config.yaml");
		if (File.Exists(text))
		{
			txtConfigPath.Text = text;
			if (showMessage)
			{
				MessageBox.Show(this, "已识别到同目录配置文件：\n" + text, "识别成功", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			}
		}
		else if (showMessage)
		{
			MessageBox.Show(this, "同目录下没有发现 config.yaml，请手动选择。", "未识别到配置", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}
	}

	private void SaveAndClose()
	{
		string text = NormalizePathOrNull(txtExecutablePath.Text);
		string text2 = NormalizePathOrNull(txtConfigPath.Text);
		if (!HasExistingFile(text))
		{
			MessageBox.Show(this, "请选择有效的 cli-proxy-api.exe。", "exe 无效", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			return;
		}
		if (!string.Equals(Path.GetFileName(text), "cli-proxy-api.exe", StringComparison.OrdinalIgnoreCase))
		{
			DialogResult dialogResult = MessageBox.Show(this, "你选择的文件名不是 cli-proxy-api.exe。\n\n如果这是你自己重命名后的可执行文件，可以继续；否则建议重新确认。", "文件名看起来不太对", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);
			if (dialogResult != DialogResult.Yes)
			{
				return;
			}
		}
		if (!HasExistingFile(text2))
		{
			MessageBox.Show(this, "请选择有效的 config.yaml。", "配置无效", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			return;
		}
		ResultSettings = new LauncherSettings
		{
			ExecutablePath = text,
			ConfigPath = text2,
			MinimizeToTrayOnClose = chkMinimizeToTray.Checked,
			AutoStartService = chkAutoStartService.Checked,
			LaunchLauncherOnWindowsStartup = chkLaunchLauncherOnWindowsStartup.Checked,
			AutoStartDelaySeconds = decimal.ToInt32(nudAutoStartDelaySeconds.Value),
			OpenManagementPageAfterStart = chkOpenManagementAfterStart.Checked
		};
		base.DialogResult = DialogResult.OK;
		Close();
	}

	private static bool HasExistingFile(string? path)
	{
		return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
	}

	private static string? NormalizePathOrNull(string? path)
	{
		return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path.Trim());
	}

	private static void OpenWithShell(string target)
	{
		Process.Start(new ProcessStartInfo
		{
			FileName = target,
			UseShellExecute = true
		});
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

	private static void ConfigureCheckBox(CheckBox checkBox, string text)
	{
		checkBox.AutoSize = true;
		checkBox.Font = new Font("Segoe UI", 10f);
		checkBox.Text = text;
	}
}
