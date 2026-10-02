using System;
using System.ComponentModel;
using SMENA.Models;

namespace SMENA.ViewModels
{
    /// <summary>Combo item for picking a rule's target task ("Project — Task").</summary>
    public class RuleTarget
    {
        public RuleTarget(Guid taskId, string label) { TaskId = taskId; Label = label; }
        public Guid TaskId { get; }
        public string Label { get; }

        // The DarkCombo template renders the raw SelectedItem via ToString, not DisplayMemberPath.
        public override string ToString() => Label;
    }

    /// <summary>One editable rules row. Wraps the stored Rule in place — every change
    /// commits straight to tasks.json (settings save immediately, like the rest).</summary>
    public class RuleRow : INotifyPropertyChanged
    {
        private readonly MainViewModel _vm;
        public Rule Rule { get; }

        public RuleRow(Rule rule, MainViewModel vm) { Rule = rule; _vm = vm; }

        public bool Enabled
        {
            get => Rule.Enabled;
            set { Rule.Enabled = value; _vm.SaveRules(); }
        }

        // A blank label shows the target task's name instead.
        public string Label => string.IsNullOrWhiteSpace(Rule.Name) ? _vm.RuleTargetLabel(Rule.TaskId) : Rule.Name!;

        public string Name
        {
            get => Rule.Name;
            set { Rule.Name = value ?? ""; _vm.SaveRules(); OnPropertyChanged(nameof(Label)); }
        }

        public string ProcessRegex
        {
            get => Rule.ProcessRegex;
            set { Rule.ProcessRegex = value ?? ""; _vm.SaveRules(); }
        }

        public string TitleRegex
        {
            get => Rule.TitleRegex;
            set { Rule.TitleRegex = value ?? ""; _vm.SaveRules(); }
        }

        public Guid TaskId
        {
            get => Rule.TaskId;
            set { Rule.TaskId = value; _vm.SaveRules(); OnPropertyChanged(nameof(Label)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
