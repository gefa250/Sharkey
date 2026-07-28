using System;
using System.Drawing;
using System.Windows.Forms;

internal static class SelectionTestHost
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var form = new Form
        {
            Text = "SharkTranslate Selection Test",
            StartPosition = FormStartPosition.CenterScreen,
            ClientSize = new Size(900, 360),
            TopMost = true
        };
        var label = new Label
        {
            Text = "The long paragraph below is selected automatically. Press F8:",
            Dock = DockStyle.Top,
            Height = 42,
            Padding = new Padding(12, 12, 0, 0)
        };
        var text = new TextBox
        {
            Text = "A translation window should remain useful for long passages. " +
                   "The complete source text must stay available instead of being truncated. " +
                   "Users need to select individual words, copy a sentence, scroll through " +
                   "multiple paragraphs, move the window away from the document, and resize it " +
                   "when they need more reading space.\r\n\r\n" +
                   "This second paragraph verifies that line wrapping and independent scrollbars " +
                   "continue to work when both the original text and the translated result are long.",
            Font = new Font("Segoe UI", 15),
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Margin = new Padding(12),
            ReadOnly = true
        };
        form.Controls.Add(text);
        form.Controls.Add(label);
        form.Shown += delegate
        {
            text.Focus();
            text.SelectAll();
        };
        Application.Run(form);
    }
}
