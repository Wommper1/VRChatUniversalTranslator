using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace VRChatTranslator.App;

public partial class MainWindow
{
    /// <summary>
    /// Smoke-test guard: every control a person can operate must have a name a screen reader can read.
    /// It walks the controls declared in the window (not the insides of control templates) and asks Windows UI Automation
    /// for each control's name, so a new input added without a label fails the startup test instead of reaching users.
    /// </summary>
    internal void RunAccessibilityChecks()
    {
        var unnamed = new List<string>();
        var checkedControls = 0;
        Visit(this);

        if (checkedControls < 60)
        {
            throw new InvalidOperationException($"The accessibility check only found {checkedControls} controls; the window layout changed or the walk is broken.");
        }

        if (unnamed.Count > 0)
        {
            throw new InvalidOperationException("These controls have no screen-reader name: " + string.Join(", ", unnamed));
        }

        void Visit(object node)
        {
            if (node is UIElement element and (ButtonBase or ComboBox or TextBox or Slider or ListBox or ProgressBar or TabItem or Expander))
            {
                checkedControls++;
                var name = UIElementAutomationPeer.CreatePeerForElement(element)?.GetName();
                if (string.IsNullOrWhiteSpace(name))
                {
                    var id = (element as FrameworkElement)?.Name;
                    unnamed.Add(string.IsNullOrEmpty(id) ? element.GetType().Name : $"{element.GetType().Name} {id}");
                }
            }

            if (node is DependencyObject dependencyObject)
            {
                foreach (var child in LogicalTreeHelper.GetChildren(dependencyObject))
                {
                    Visit(child);
                }
            }
        }
    }
}
