using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Plugin.UI;

public partial class CortexPanel : Page
{
    private static CortexPanel? _instance;
    private readonly ObservableCollection<ChatMessage> _messages = new();
    private readonly ObservableCollection<PromptChip> _chips = new();
    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _chipsTimer;
    private CortexChatClient? _client;
    private bool _isProcessing;
    private bool _lastStatus;

    private static readonly SolidColorBrush BrushOnline = new(Color.FromRgb(76, 175, 80));
    private static readonly SolidColorBrush BrushOffline = new(Color.FromRgb(244, 67, 54));
    private static readonly SolidColorBrush BrushOfflineText = new(Color.FromRgb(136, 136, 136));

    public static CortexPanel? Instance => _instance;

    public CortexPanel()
    {
        InitializeComponent();
        _instance = this;
        ChatMessages.ItemsSource = _messages;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statusTimer.Tick += (s, e) => UpdateStatus();

        PromptChips.ItemsSource = _chips;
        _chipsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _chipsTimer.Tick += (s, e) => UpdateChips();

        ChatInput.TextChanged += (s, e) =>
        {
            Placeholder.Visibility = string.IsNullOrEmpty(ChatInput.Text)
                ? Visibility.Visible : Visibility.Collapsed;
        };

        AddMessage("assistant",
            "Hi! I'm your RevitCortex assistant.\n\n" +
            "I have direct access to the open model and can perform operations in real time. " +
            "Ask me anything about the project or tell me what to do.");
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _statusTimer.Start();
        _chipsTimer.Start();
        UpdateStatus();
        UpdateChips();
        ChatInput.Focus();
    }

    private void UpdateStatus()
    {
        try
        {
            bool running = RevitCortexApp.Instance?.IsServiceRunning ?? false;
            if (running == _lastStatus) return;
            _lastStatus = running;
            StatusIndicator.Fill = running ? BrushOnline : BrushOffline;
            StatusText.Text = running ? "Online" : "Offline";
            StatusText.Foreground = running ? BrushOnline : BrushOfflineText;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[RevitCortex] Status update error: {ex.Message}");
        }
    }

    private void ChatInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && !_isProcessing)
        {
            Send_Click(sender, e);
            e.Handled = true;
        }
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        string? input = ChatInput.Text?.Trim();
        if (string.IsNullOrEmpty(input) || _isProcessing) return;

        ChatInput.Text = "";
        AddMessage("user", input);

        _isProcessing = true;
        SendButton.IsEnabled = false;
        TypingIndicator.Visibility = Visibility.Visible;
        TypingText.Text = "Thinking...";

        try
        {
            if (!(RevitCortexApp.Instance?.IsServiceRunning ?? false))
            {
                AddMessage("assistant",
                    "The server is not running. Click \"RevitCortex Switch\" in the ribbon to start it.");
                return;
            }

            _client ??= new CortexChatClient();
            string response = await _client.SendMessage(input);
            AddMessage("assistant", response);
        }
        catch (Exception ex)
        {
            AddMessage("assistant", $"An error occurred: {ex.Message}");
        }
        finally
        {
            _isProcessing = false;
            SendButton.IsEnabled = true;
            TypingIndicator.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateChips()
    {
        try
        {
            if (!(RevitCortexApp.Instance?.IsServiceRunning ?? false))
            {
                _chips.Clear();
                ChipsBar.Visibility = Visibility.Collapsed;
                return;
            }

            var uiApp = RevitCortexApp.Instance?.UiApplication;
            if (uiApp?.ActiveUIDocument == null)
            {
                SetChips(new[] { new PromptChip("Open a project to get started") });
                return;
            }

            var doc = uiApp.ActiveUIDocument.Document;
            var activeView = doc.ActiveView;
            var selection = uiApp.ActiveUIDocument.Selection;
            int selectedCount = selection.GetElementIds().Count;

            var chips = new List<PromptChip>();

            if (selectedCount > 0)
            {
                chips.Add(new PromptChip($"Show parameters ({selectedCount} selected)", "Show me the parameters of the selected elements"));
                chips.Add(new PromptChip("Isolate in view", "Isolate the selected elements in the current view"));
                chips.Add(new PromptChip("Measure distance", "Measure the distance between the selected elements"));
            }

            if (activeView is Autodesk.Revit.DB.ViewPlan)
            {
                bool hasRooms = new Autodesk.Revit.DB.FilteredElementCollector(doc, activeView.Id)
                    .OfCategory(Autodesk.Revit.DB.BuiltInCategory.OST_Rooms)
                    .GetElementCount() > 0;

                if (hasRooms)
                {
                    chips.Add(new PromptChip("Tag all rooms", "Tag all rooms in the current view"));
                    chips.Add(new PromptChip("Color rooms by department", "Create a color legend for rooms by department"));
                    chips.Add(new PromptChip("Export room data", "Export all room data"));
                }
                else
                {
                    chips.Add(new PromptChip("Check model health", "Check the health of this model"));
                    chips.Add(new PromptChip("Show warnings", "Show me all model warnings"));
                    chips.Add(new PromptChip("Export to Excel", "Export all elements in this view to Excel"));
                }
            }
            else if (activeView is Autodesk.Revit.DB.View3D)
            {
                chips.Add(new PromptChip("Check model health", "Check the health of this model"));
                chips.Add(new PromptChip("Detect clashes", "Check for clashes between structural elements and MEP"));
                chips.Add(new PromptChip("Audit families", "Audit all families in this project"));
            }
            else if (activeView is Autodesk.Revit.DB.ViewSheet)
            {
                chips.Add(new PromptChip("Align viewports", "Align all viewports on this sheet"));
                chips.Add(new PromptChip("Export to PDF", "Export all sheets to PDF"));
            }
            else if (activeView is Autodesk.Revit.DB.ViewSchedule)
            {
                chips.Add(new PromptChip("Export schedule to CSV", "Export this schedule to CSV"));
                chips.Add(new PromptChip("Export to Excel", "Export this schedule to Excel"));
            }

            if (chips.Count == 0 || (selectedCount == 0 && chips.Count < 3))
            {
                chips.Add(new PromptChip("Model statistics", "How many elements are in this model? Give me statistics by category"));
                chips.Add(new PromptChip("Check model health", "Check the health of this model"));
                chips.Add(new PromptChip("List warnings", "Show me all model warnings"));
            }

            SetChips(chips.Take(6));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[RevitCortex] Chips update error: {ex.Message}");
            _chips.Clear();
            ChipsBar.Visibility = Visibility.Collapsed;
        }
    }

    private void SetChips(IEnumerable<PromptChip> chips)
    {
        _chips.Clear();
        foreach (var chip in chips)
            _chips.Add(chip);
        ChipsBar.Visibility = _chips.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Chip_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is PromptChip chip && !_isProcessing)
        {
            ChatInput.Text = chip.Prompt;
            Send_Click(sender, e);
        }
    }

    private void AddMessage(string role, string text)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _messages.Add(new ChatMessage(role, text));
            ChatScrollViewer.ScrollToEnd();
        }));
    }

    public void OnToolExecuting(string toolName)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            TypingText.Text = $"Running {toolName}...";
            _messages.Add(new ChatMessage("tool", $"-> {toolName}"));
            ChatScrollViewer.ScrollToEnd();
        }));
    }

    public void OnToolCompleted(string toolName, bool isError, string result)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            string preview = result.Length > 200 ? result.Substring(0, 200) + "..." : result;
            string status = isError ? $"x {toolName} -- error:\n{preview}" : $"ok {toolName} completed";
            _messages.Add(new ChatMessage(isError ? "tool_error" : "tool_ok", status));
            ChatScrollViewer.ScrollToEnd();
        }));
    }

    public void OnIntermediateText(string text)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _messages.Add(new ChatMessage("assistant", text));
            ChatScrollViewer.ScrollToEnd();
        }));
    }

    public void OnRoundProgress(int current, int max)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            TypingText.Text = $"Processing... (step {current}/{max})";
        }));
    }

    public void OnRetrying(int seconds)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            TypingText.Text = $"Rate limit -- retrying in {seconds}s...";
        }));
    }

    public void OnThinkingReceived(string thinkingText)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            int charCount = thinkingText.Length;
            string firstLine = thinkingText.Split('\n')[0];
            if (firstLine.Length > 120)
                firstLine = firstLine.Substring(0, 120) + "...";
            string summary = $"{firstLine}\n[{charCount:N0} chars of reasoning]";
            _messages.Add(new ChatMessage("thinking", summary));
            ChatScrollViewer.ScrollToEnd();
        }));
    }

    private void StopButton_Click(object sender, MouseButtonEventArgs e)
    {
        _client?.Cancel();
        TypingText.Text = "Cancelling...";
    }

    /// <summary>
    /// Called when the active document is closing. Resets chat and client state
    /// so commands and context from one document don't leak into another.
    /// </summary>
    public void OnDocumentClosing()
    {
        _messages.Clear();
        _client?.ClearHistory();
        _client = null; // Force new client with fresh conversation on next document
        _chips.Clear();
        ChipsBar.Visibility = Visibility.Collapsed;
        _isProcessing = false;
        SendButton.IsEnabled = true;
        TypingIndicator.Visibility = Visibility.Collapsed;
        AddMessage("assistant",
            "Document closed. Open a new document and click \"Cortex Switch\" to reconnect.");
    }

    private void ClearChat_Click(object sender, MouseButtonEventArgs e)
    {
        _messages.Clear();
        _client?.ClearHistory();
        AddMessage("assistant", "Chat cleared. How can I help you?");
    }

    private void ExportChat_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export chat",
                FileName = $"cortex_chat_{DateTime.Now:yyyyMMdd_HHmmss}",
                DefaultExt = ".txt",
                Filter = "Text (*.txt)|*.txt|Markdown (*.md)|*.md|JSON (*.json)|*.json",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (dialog.ShowDialog() != true) return;

            string content = dialog.FilterIndex switch
            {
                2 => BuildMarkdown(),
                3 => BuildJson(),
                _ => BuildPlainText()
            };

            File.WriteAllText(dialog.FileName, content, Encoding.UTF8);
            AddMessage("assistant", $"Chat exported to:\n{dialog.FileName}");
        }
        catch (Exception ex)
        {
            AddMessage("assistant", $"Export error: {ex.Message}");
        }
    }

    private string BuildPlainText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"RevitCortex Chat -- {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine();
        foreach (var msg in _messages)
        {
            sb.AppendLine($"[{msg.RoleLabel}]");
            sb.AppendLine(msg.Text);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private string BuildMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# RevitCortex Chat -- {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        foreach (var msg in _messages)
        {
            sb.AppendLine($"**{msg.RoleLabel}**");
            sb.AppendLine();
            sb.AppendLine(msg.Text);
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private string BuildJson()
    {
        var array = new JArray();
        foreach (var msg in _messages)
            array.Add(new JObject
            {
                ["role"] = msg.RoleLabel,
                ["text"] = msg.Text
            });
        return new JObject
        {
            ["exported"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["messages"] = array
        }.ToString(Newtonsoft.Json.Formatting.Indented);
    }
}

public class PromptChip
{
    public string Text { get; set; }
    public string Prompt { get; set; }

    public PromptChip(string text, string? prompt = null)
    {
        Text = text;
        Prompt = prompt ?? text;
    }
}

public class ChatMessage
{
    public string Role { get; }
    public string Text { get; }
    public string RoleLabel { get; }
    public string AvatarLetter { get; }
    public SolidColorBrush AvatarBackground { get; }
    public SolidColorBrush RoleLabelColor { get; }
    public SolidColorBrush TextColor { get; }
    public SolidColorBrush RowBackground { get; }
    public FontFamily FontFamily { get; }

    // ── Static frozen brushes (shared across all ChatMessage instances) ──
    private static readonly SolidColorBrush CortexTeal = Freeze(new SolidColorBrush(Color.FromRgb(0, 131, 143)));
    private static readonly SolidColorBrush UserIndigo = Freeze(new SolidColorBrush(Color.FromRgb(92, 107, 192)));
    private static readonly SolidColorBrush ToolGreen = Freeze(new SolidColorBrush(Color.FromRgb(76, 175, 80)));
    private static readonly SolidColorBrush ThinkingPurple = Freeze(new SolidColorBrush(Color.FromRgb(126, 87, 194)));
    private static readonly SolidColorBrush ErrorRed = Freeze(new SolidColorBrush(Color.FromRgb(244, 67, 54)));

    private static readonly SolidColorBrush BrushDark = Freeze(new SolidColorBrush(Color.FromRgb(33, 33, 33)));
    private static readonly SolidColorBrush BrushGray55 = Freeze(new SolidColorBrush(Color.FromRgb(55, 55, 55)));
    private static readonly SolidColorBrush BrushGreenDark = Freeze(new SolidColorBrush(Color.FromRgb(46, 125, 50)));
    private static readonly SolidColorBrush BrushPurpleText = Freeze(new SolidColorBrush(Color.FromRgb(120, 100, 160)));
    private static readonly SolidColorBrush BrushErrorDark = Freeze(new SolidColorBrush(Color.FromRgb(183, 28, 28)));

    private static readonly SolidColorBrush BgWhite = Freeze(new SolidColorBrush(Colors.White));
    private static readonly SolidColorBrush BgGrayLight = Freeze(new SolidColorBrush(Color.FromRgb(248, 248, 248)));
    private static readonly SolidColorBrush BgThinking = Freeze(new SolidColorBrush(Color.FromRgb(245, 243, 250)));
    private static readonly SolidColorBrush BgToolOk = Freeze(new SolidColorBrush(Color.FromRgb(243, 250, 243)));
    private static readonly SolidColorBrush BgToolError = Freeze(new SolidColorBrush(Color.FromRgb(255, 245, 245)));

    private static readonly FontFamily FontSegoeUI = new FontFamily("Segoe UI");
    private static readonly FontFamily FontConsolas = new FontFamily("Consolas");

    private static SolidColorBrush Freeze(SolidColorBrush brush) { brush.Freeze(); return brush; }

    public ChatMessage(string role, string text)
    {
        Role = role;
        Text = text;

        switch (role)
        {
            case "user":
                RoleLabel = "You";
                AvatarLetter = "U";
                AvatarBackground = UserIndigo;
                RoleLabelColor = BrushDark;
                TextColor = BrushDark;
                RowBackground = BgWhite;
                FontFamily = FontSegoeUI;
                break;
            case "thinking":
                RoleLabel = "Planning";
                AvatarLetter = "?";
                AvatarBackground = ThinkingPurple;
                RoleLabelColor = ThinkingPurple;
                TextColor = BrushPurpleText;
                RowBackground = BgThinking;
                FontFamily = FontSegoeUI;
                break;
            case "tool":
                RoleLabel = "";
                AvatarLetter = ">";
                AvatarBackground = ToolGreen;
                RoleLabelColor = ToolGreen;
                TextColor = BrushGreenDark;
                RowBackground = BgGrayLight;
                FontFamily = FontConsolas;
                break;
            case "tool_ok":
                RoleLabel = "";
                AvatarLetter = "v";
                AvatarBackground = ToolGreen;
                RoleLabelColor = ToolGreen;
                TextColor = BrushGreenDark;
                RowBackground = BgToolOk;
                FontFamily = FontSegoeUI;
                break;
            case "tool_error":
                RoleLabel = "";
                AvatarLetter = "x";
                AvatarBackground = ErrorRed;
                RoleLabelColor = ErrorRed;
                TextColor = BrushErrorDark;
                RowBackground = BgToolError;
                FontFamily = FontConsolas;
                break;
            default: // assistant
                RoleLabel = "Cortex";
                AvatarLetter = "RC";
                AvatarBackground = CortexTeal;
                RoleLabelColor = BrushDark;
                TextColor = BrushGray55;
                RowBackground = BgGrayLight;
                FontFamily = FontSegoeUI;
                break;
        }
    }
}
