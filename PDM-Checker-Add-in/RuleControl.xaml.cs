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
using static PDM_Checker_Add_in.UserControl1;

namespace PDM_Checker_Add_in
{
    /// <summary>
    /// Interaction logic for UserControl2.xaml
    /// </summary>
    public partial class RuleControl : UserControl
    {

        public ObservableCollection<VariableRules> VariableRulesCollection = new ObservableCollection<VariableRules>();

        //List
        public List<VariableRules> VariableRuleList = new List<VariableRules>();
        public VariableConditionCheck varConditionCheck {  get; set; }
        public UserControl1 UserControl1 { get; set; }

        public ObservableCollection<string> VariableCollection { get; set; }

        public IEnumerable<ConditionRule> ConditionRuleEnumValues
        {
            get
            {
                return Enum.GetValues(typeof(ConditionRule)) as IEnumerable<ConditionRule>;
            }
        }


        public RuleControl()
        {
            InitializeComponent();
            RuleList.ItemsSource = VariableRulesCollection;
        }

        public void LoadRules()
        {
            if(VariableRuleList != null)
            {
                foreach (var rule in VariableRuleList)
                {
                    VariableRulesCollection.Add(rule);
                }
            }
        }

        private void Btn_AddNewVariableCondition(object sender, RoutedEventArgs e)
        {
            VariableRulesCollection.Add(new VariableRules() { condition = ConditionRule.NotEmpty, variable = "", ruleText = "", message= "" });
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            VariableRuleList = VariableRulesCollection.ToList();

            UserControl1.SaveRules(VariableRuleList, varConditionCheck);

            Window.GetWindow(this).Close();
            GC.Collect();
        }

        private void DeleteRule(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            ListViewItem listViewItem = FindAncestor<ListViewItem>(button);
            VariableRules variableRules = listViewItem.DataContext as VariableRules;

            VariableRulesCollection.Remove(variableRules);
        }

        private void CancelBtn(object sender, RoutedEventArgs e)
        {
            Window.GetWindow(this).Close();
            GC.Collect();
        }
    }
}
