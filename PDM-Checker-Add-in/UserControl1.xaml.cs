using BlueByte.SOLIDWORKS.PDMProfessional.Extensions;
using BlueByte.SOLIDWORKS.PDMProfessional.SDK;
using BlueByte.SOLIDWORKS.PDMProfessional.SDK.Attributes;
using BlueByte.SOLIDWORKS.PDMProfessional.SDK.Diagnostics;
using EPDM.Interop.epdm;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace PDM_Checker_Add_in
{
    /// <summary>
    /// Interaction logic for UserControl1.xaml
    /// </summary>
    public partial class UserControl1 : UserControl
    {
        public IEdmWorkflow5[] workflows;
        private IEdmState7[] states;
        public IEdmVault7 Vault;
        public Setup setup = new Setup();

        public string[] variables;
        public string[] skippedOnCopyVariables;

        public enum ConditionRule
        {
            FileName, FileNameOrEmpty, TextEqual, Regex, NotEmpty, Variable, TextContain, TextNotEqual, TextDoesntContain
        }
        public class VariableToUse
        {
            public string Variable { get; set; }
            public bool isUsed { get; set; }
        }
        public class SkipDatacardVariable
        {
            public string Variable { get; set; }
            public bool isSkipped { get; set; }
        }
        public class IgnoreFilenameInState
        {
            public string stateName { get; set; }
            public bool isIgnored { get; set; }
        }
        public class IgnoreStateChange
        {
            public string stateName { get; set; }
            public bool isIgnored { get; set; }
        }
        public class CheckOnlyInState
        {
            public string stateName { get; set; }
            public bool isChecked { get; set; }
        }

        public class VariableConditionCheck
        {
            public string variable { get; set;}
            public string condition { get; set;}
            public List<VariableRules> rules { get; set;}
        }
        public class VariableRules
        {
            public string variable { get; set; }
            public ConditionRule condition { get; set;}
            public string ruleText { get; set;}
            public string message {  get; set;}
        }

        List<VariableToUse> VariablesToUseList = new List<VariableToUse>();
        List<SkipDatacardVariable> SkipDatacardVariableList = new List<SkipDatacardVariable>();
        List<CheckOnlyInState> CheckOnlyInStateList = new List<CheckOnlyInState>();
        List<IgnoreFilenameInState> IgnoreFilenameInStateList = new List<IgnoreFilenameInState>();
        List<VariableConditionCheck> VariableConditionCheckList = new List<VariableConditionCheck>();
        List<IgnoreStateChange> IgnoreStateChangeList = new List<IgnoreStateChange>();

        public ObservableCollection<string> VariableCollection { get; set; }

        public IEdmDictionary5 dic;

        public UserControl1()
        {
            InitializeComponent();
        }

        public void LoadSetup()
        {
            VariablesToUseList.Clear();
            SkipDatacardVariableList.Clear();
            CheckOnlyInStateList.Clear();
            IgnoreFilenameInStateList.Clear();
            variableConditionChecks.Clear();
            IgnoreStateChangeList.Clear();

            dic.LongGetAt(1, out var pbsRetValue);
            setup = JsonConvert.DeserializeObject<Setup>(pbsRetValue);
            List<string> workflowList = new List<string>();
            workflowList.Add(setup.workflow.ToString());
            ChooseWorkflow.ItemsSource = workflowList;
            ChooseWorkflow.SelectedValue = setup.workflow;

            CheckDrawingCustomConfig.IsChecked = setup.checkOnlyCustomInDrawing;
            CheckCustomConfig.IsChecked = setup.checkCustomConfig;

            VariableCollection = new ObservableCollection<string>();

            //combobox variable list
            foreach (var item in setup.variables.ToList())
            {
                VariableCollection.Add(item);
            }

            if (ChooseWorkflow.SelectedValue.ToString() != "" | ChooseWorkflow.SelectedValue.ToString() != null)
            {
                GetStates();
                List<string> stateList = new List<string>();
                foreach(var state in states)
                {
                    if (setup.checkInStates != null)
                    {
                        bool isChecked = setup.checkInStates.Contains(state.Name);
                        CheckOnlyInStateList.Add(new CheckOnlyInState() { stateName = state.Name, isChecked = isChecked });
                    }
                    else
                    {
                        CheckOnlyInStateList.Add(new CheckOnlyInState() { stateName = state.Name, isChecked = false });
                    }

                    if (setup.ignoreFilenameInStates != null)
                    {
                        bool isIgnored = setup.ignoreFilenameInStates.Contains(state.Name);
                        IgnoreFilenameInStateList.Add(new IgnoreFilenameInState() { stateName = state.Name, isIgnored = isIgnored });
                    }
                    else
                    {
                        IgnoreFilenameInStateList.Add(new IgnoreFilenameInState() { stateName = state.Name, isIgnored = false });
                    }

                    if (setup.ignoreStateChangeTo != null)
                    {
                        bool isIgnored = setup.ignoreStateChangeTo.Contains(state.Name);
                        IgnoreStateChangeList.Add(new IgnoreStateChange() { stateName = state.Name, isIgnored = isIgnored });
                    }
                    else
                    {
                        IgnoreStateChangeList.Add(new IgnoreStateChange() { stateName = state.Name, isIgnored = false });
                    }
                }
            }

            enableStateChange.IsChecked = setup.enableStateChangeChecks;
            enableCardBtn.IsChecked = setup.enableCardButton;

            foreach (var variable in variables)
            {
                //Get dictionary values in or set to false
                if (setup.variables != null)
                { 
                    bool isChecked = setup.variables.Contains(variable);
                    VariablesToUseList.Add(new VariableToUse() { Variable = variable, isUsed = isChecked });
                }
                else
                {
                    VariablesToUseList.Add(new VariableToUse() { Variable = variable, isUsed = false });
                }

                if (setup.skippedOnCopyVariables != null)
                {
                    bool isSkipped = setup.skippedOnCopyVariables.Contains(variable);
                    SkipDatacardVariableList.Add(new SkipDatacardVariable() { Variable = variable, isSkipped = isSkipped });
                }
                else
                {
                    SkipDatacardVariableList.Add(new SkipDatacardVariable() { Variable = variable, isSkipped = false });
                }
            }

            CheckOnlyInStateList = CheckOnlyInStateList.OrderBy(name => name.stateName).ToList();
            IgnoreFilenameInStateList = IgnoreFilenameInStateList.OrderBy(name => name.stateName).ToList();

            IgnoreStateChangeList = IgnoreStateChangeList.OrderBy(name => name.stateName).ToList();
            IgnoreChangeState.ItemsSource = IgnoreStateChangeList;

            UsedVariables.ItemsSource = VariablesToUseList;
            skipWhenCopy.ItemsSource = SkipDatacardVariableList;
            StatesToCheck.ItemsSource = CheckOnlyInStateList;
            IgnoreFilenameInStates.ItemsSource = IgnoreFilenameInStateList;

            if(setup.variableChecks != null)
            {
                foreach(var check in setup.variableChecks)
                {
                    variableConditionChecks.Add(check);
                }

                VariableConditionList.ItemsSource = variableConditionChecks;

            } else
            {
                VariableConditionList.ItemsSource = variableConditionChecks;
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Window.GetWindow(this).Close();
            GC.Collect();
        }
        private void ChooseWorkflow_DropDownOpened(object sender, EventArgs e)
        {
            if (ChooseWorkflow.Items.Count > 1) { return; }
            List<string> workflowNames = new List<string>();
            foreach (var workflow in workflows)
            {
                var workflowName = Vault.GetObject(EdmObjectType.EdmObject_Workflow, workflow.InitialState.WorkflowID);
                workflowNames.Add(workflowName.Name);
            }

            ChooseWorkflow.ItemsSource = workflowNames;
        }

        private void GetStates()
        {
            IEdmWorkflowMgr6 WorkflowMgr = default(IEdmWorkflowMgr6);
            WorkflowMgr = (IEdmWorkflowMgr6)Vault.CreateUtility(EdmUtility.EdmUtil_WorkflowMgr);

            var wfPos = WorkflowMgr.GetFirstWorkflowPosition();
            IEdmWorkflow6 wf;

            List<IEdmState7> stateList = new List<IEdmState7>();

            IEdmPos5 statePos = default(IEdmPos5);

            while (!wfPos.IsNull)
            {
                wf = WorkflowMgr.GetNextWorkflow(wfPos);

                if (wf.Name == ChooseWorkflow.SelectedValue.ToString())
                {
                    statePos = wf.GetFirstStatePosition();
                    while (!statePos.IsNull)
                    {
                        IEdmState7 state = (IEdmState7)wf.GetNextState(statePos);
                        stateList.Add(state);
                    }

                }
            }

            states = stateList.ToArray();
        }

        private void Button_Save(object sender, RoutedEventArgs e)
        {
            setup.workflow = ChooseWorkflow.SelectedValue.ToString();
            List<string> variableList = new List<string>();
            List<string> skippedList = new List<string>();
            List<string> checkInStateList = new List<string>();
            List<string> ignoreFilenameInStatesList = new List<string>();
            List<string> ignoreStateChangesToList = new List<string>();

            setup.enableCardButton = enableCardBtn.IsChecked;
            setup.enableStateChangeChecks = enableStateChange.IsChecked;

            setup.checkOnlyCustomInDrawing = CheckDrawingCustomConfig.IsChecked;
            setup.checkCustomConfig = CheckCustomConfig.IsChecked;

            VariableConditionCheckList = variableConditionChecks.ToList();

            setup.variableChecks = VariableConditionCheckList;

            foreach (var datacardVariable in SkipDatacardVariableList)
            {
                if (datacardVariable.isSkipped)
                {
                    skippedList.Add(datacardVariable.Variable);
                }
            }

            foreach (var item in IgnoreStateChangeList)
            {
                if (item.isIgnored)
                {
                    ignoreStateChangesToList.Add(item.stateName);
                }
            }

            foreach(var item in VariablesToUseList)
            {
                if (item.isUsed)
                {
                    variableList.Add(item.Variable);
                }
            }

            foreach (var item in CheckOnlyInStateList)
            {
                if (item.isChecked)
                {
                    checkInStateList.Add(item.stateName);
                }
            }

            foreach (var item in IgnoreFilenameInStateList)
            {
                if (item.isIgnored)
                {
                    ignoreFilenameInStatesList.Add(item.stateName);
                }
            }

            setup.variables = variableList.ToArray();
            setup.skippedOnCopyVariables = skippedList.ToArray();
            setup.checkInStates = checkInStateList.ToArray();
            setup.ignoreFilenameInStates = ignoreFilenameInStatesList.ToArray();
            setup.ignoreStateChangeTo = ignoreStateChangesToList.ToArray();

            string jsonString = JsonConvert.SerializeObject(setup);

            var gotSet = dic.LongTestAndSetAt(1, jsonString);
            if (!gotSet)
            {
                dic.LongSetAt(1, jsonString);
            }
        }

        public ObservableCollection<VariableConditionCheck> variableConditionChecks = new ObservableCollection<VariableConditionCheck>();
        private void Btn_AddVariableConditionCheck(object sender, RoutedEventArgs e)
        {
            variableConditionChecks.Add(new VariableConditionCheck() { variable = "", condition = "", rules = null });
        }

        private void Btn_EditRules(object sender, RoutedEventArgs e)
        {
            //Get/Set rules
            Button button = sender as Button;
            ListViewItem listViewItem = FindAncestor<ListViewItem>(button);
            VariableConditionCheck varConditionCheck = listViewItem.DataContext as VariableConditionCheck;

            //open user control
            var ruleControl = new RuleControl() { VariableRuleList = varConditionCheck.rules, varConditionCheck = varConditionCheck, UserControl1 = this, VariableCollection = VariableCollection };
            ruleControl.LoadRules();
            Window window = new Window
            {
                Title = "Set up rules",
                Content = ruleControl,
                Width = 400,
                Height = 800,
            };

            window.Show();
        }

        public void SaveRules(List<VariableRules> rules, VariableConditionCheck varConditionCheck)
        {
            var index = variableConditionChecks.IndexOf(varConditionCheck);
            varConditionCheck.rules = rules;
            variableConditionChecks[index] = varConditionCheck;
            VariableConditionCheckList = variableConditionChecks.ToList();

            setup.variableChecks = VariableConditionCheckList;
        }

        private void VariableComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var comboBox = sender as ComboBox;
            ListViewItem listViewItem = FindAncestor<ListViewItem>(comboBox);
            VariableConditionCheck varConditionCheck = listViewItem.DataContext as VariableConditionCheck;

            varConditionCheck.variable = comboBox.SelectedItem as string;
        }

        private void DeleteVariableCondition(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            ListViewItem listViewItem = FindAncestor<ListViewItem>(button);
            VariableConditionCheck varConditionCheck = listViewItem.DataContext as VariableConditionCheck;

            variableConditionChecks.Remove(varConditionCheck);
        }

        public static T FindAncestor<T>(DependencyObject dependencyObject) where T : DependencyObject
        {
            do
            {
                if (dependencyObject is T)
                {
                    return (T)dependencyObject;
                }
                dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
            } while (dependencyObject != null);
            return null;
        }

    }
}
