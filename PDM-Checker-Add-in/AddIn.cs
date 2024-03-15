using BlueByte.SOLIDWORKS.PDMProfessional.Extensions;
using BlueByte.SOLIDWORKS.PDMProfessional.SDK;
using BlueByte.SOLIDWORKS.PDMProfessional.SDK.Attributes;
using BlueByte.SOLIDWORKS.PDMProfessional.SDK.Diagnostics;
using EPDM.Interop.epdm;
using Newtonsoft.Json;
using SimpleInjector;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using static PDM_Checker_Add_in.UserControl1;

namespace PDM_Checker_Add_in
{
    public enum Commands
    {
        SetupMenu = 1000
    }

    public class Setup
    {
        public string workflow;
        public string state;
        public string[] variables;
        public string[] skippedOnCopyVariables;
        public bool? enableStateChangeChecks;
        public bool? enableCardButton;
        public bool? checkCustomConfig;
        public bool? checkOnlyCustomInDrawing;
        public string[] checkInStates;
        public string[] ignoreFilenameInStates;
        public string[] ignoreStateChangeTo;
        public List<VariableConditionCheck> variableChecks;
    }

    //[Menu((int)Commands.CommandOne, "Set up add-in")]
    [ListenFor(EdmCmdType.EdmCmd_PreLock)]
    [ListenFor(EdmCmdType.EdmCmd_PreUnlock)]
    [ListenFor(EdmCmdType.EdmCmd_PreState)]
    [ListenFor(EdmCmdType.EdmCmd_CardButton)]
    [Menu((int)Commands.SetupMenu, "Set up add-in", 512)]
    //[Menu((int)Commands.SetupMenu, "Set up add-in")]
    [Name("PDM Checker Add-in")]
    [Description("Checks integration. Blocks wrong revisions, non-unique filenames, and wrong Part numbers \n This program uses PDMProfessionalExtensions. PDMProfessionalExtensions is a copyrighted work of Blue Byte Systems, Inc. (R). https://bluebyte.biz")]
    [CompanyName("Company")]
    [AddInVersion(false, 9)]
    [IsTask(false)]
    [RequiredVersion(10, 0)]
    [ComVisible(true)]
    [Guid("6a208acb-668e-41f1-b69f-e59823d4531f")]
    public partial class AddIn : AddInBase
    {
        public Setup setup = new Setup();
        public IEdmDictionary5 dic;
        public PDMMethods PDMMethods = new PDMMethods();

        private int _windowHandle;
        public int WindowHandle
        {
            get { return _windowHandle; }
            set => _windowHandle = value;
        }

        private IEdmFolder5 logFolder;

        public override void OnCmd(ref EdmCmd poCmd, ref EdmCmdData[] ppoData)
        {
            base.OnCmd(ref poCmd, ref ppoData);

            WindowHandle = poCmd.mlParentWnd;

            var logFolderPath = System.IO.Path.Combine(this.Vault.RootFolder.LocalPath, "Logs", "PDM-Add-in");
            logFolder = this.Vault.TryGetFolderFromPath(logFolderPath);

            dic = Vault.GetDictionary("PDM_Checker_Add_in", true);

            try
            {

                #region State change and check-in cmd
                if (poCmd.meCmdType == EdmCmdType.EdmCmd_PreLock | poCmd.meCmdType == EdmCmdType.EdmCmd_PreState)
                {
                    bool isChangingState = poCmd.meCmdType == EdmCmdType.EdmCmd_PreState;
                    bool isCheckingOut = poCmd.meCmdType == EdmCmdType.EdmCmd_PreLock;

                    dic.LongGetAt(1, out var pbsRetValue);
                    setup = JsonConvert.DeserializeObject<Setup>(pbsRetValue);

                    if (setup.enableStateChangeChecks == null | setup.enableStateChangeChecks == false)
                    {
                        return;
                    }

                    var stringBuilder = new StringBuilder();

                    bool integrationError = false;

                    if (setup == null)
                    {
                        return;
                    }

                    IEdmVault21 vault = new EdmVault5() as IEdmVault21;
                    vault.LoginAuto(Vault.Name, 0); //Vault.CreateSearch won't work

                    string stateChangeTo = "";

                    if (isChangingState)
                    {
                        stateChangeTo = ppoData[0].mbsStrData2;
                    }
                    ForEachFile(ref ppoData, (IEdmFile5 file) =>
                    {
                        if (file.Name.Contains("^"))
                        {
                            return;
                        }

                        var currentState = file.CurrentState as IEdmState6;
                        var workflow = Vault.GetObject(EdmObjectType.EdmObject_Workflow, currentState.WorkflowID) as IEdmWorkflow6;

                        if (string.IsNullOrEmpty(stringBuilder.ToString()) == false)
                        {
                            stringBuilder.AppendLine("");
                        }

                        if (setup.workflow == workflow.Name && setup.checkInStates.Contains(currentState.Name) && !setup.ignoreStateChangeTo.Contains(stateChangeTo))
                        {
                            var fileName = file.Name;
                            var filenameExType = fileName.Remove(fileName.Length - 7).ToUpper();

                            List<string> configurations = new List<string>();

                            try
                            {
                                configurations = file.GetConfigurationNames(0).ToList();
                                configurations.Add("@");
                            }
                            catch (Exception ex)
                            {
                                OnCreateErrorLog(ex, file, WindowHandle, logFolder);
                                return;
                            }

                            if (setup.variables != null & isChangingState)
                            {
                                var configurationVariables = file.BatchGetVariables(setup.variables);
                                //Go through condition rules

                                foreach (var config in configurations)
                                {
                                    if (!(bool)setup.checkCustomConfig & config == "@" & !file.IsDrawing())
                                    {
                                        continue;
                                    }
                                    else if ((bool)setup.checkOnlyCustomInDrawing & file.IsDrawing() & config != "@")
                                    {
                                        continue;
                                    }
                                    configurationVariables.TryGetValue(config, out var variables);

                                    foreach (var variableCheck in setup.variableChecks)
                                    {
                                        variables.TryGetValue(variableCheck.variable, out var variableValue);

                                        if (variableCheck.condition != "")
                                        {
                                            if (string.IsNullOrEmpty((string)variableValue))
                                            {
                                                variableValue = "";
                                            }

                                            if (variableCheck.condition == variableValue.ToString())
                                            {
                                                foreach (var rule in variableCheck.rules)
                                                {
                                                    try
                                                    {
                                                        ConditionChecks(rule, variables, fileName, filenameExType, ref integrationError, stringBuilder, config);
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        OnCreateErrorLog(ex, file, WindowHandle, logFolder);
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                continue;
                                            }

                                        }
                                        else
                                        {
                                            foreach (var rule in variableCheck.rules)
                                            {
                                                try
                                                {
                                                    ConditionChecks(rule, variables, fileName, filenameExType, ref integrationError, stringBuilder, config);
                                                }
                                                catch (Exception ex)
                                                {
                                                    OnCreateErrorLog(ex, file, WindowHandle, logFolder);
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            #region Check non-unique filename before state change & check revision
                            //Block non-unique filenames from state change
                            if (!fileName.ToUpper().Contains(".SLDDRW") & isChangingState)
                            {
                                IEdmSearch9 search = vault.CreateSearch2() as IEdmSearch9;
                                search.FindFiles = true;
                                search.FindFolders = false;
                                search.SetToken(EdmSearchToken.Edmstok_AllVersions, false);
                                search.FileName = filenameExType + ".sld";

                                var result = search.GetFirstResult();

                                string partOrAssemblyRevision = (string)file.GetVariableFromDb("Revision", file.GetConfigurationNames()[0]);

                                while (result != null)
                                {
                                    if (result.Name.ToUpper() == fileName.ToUpper() | result.Name.ToUpper() == filenameExType.ToUpper() + ".SLDDRW")
                                    {
                                        string drawingRevision = (string)Vault.GetFileFromPath(result.Path, out var ppoRetParFolder).GetVariableFromDb("Revision", "@");

                                        if (result.Path.ToLower().EndsWith(".slddrw"))
                                        {
                                            var drawingEmptyOr0 = string.IsNullOrEmpty(drawingRevision);
                                            var partEmptyOr0 = string.IsNullOrEmpty(partOrAssemblyRevision);

                                            if (partEmptyOr0)
                                            {
                                                partOrAssemblyRevision = "00";
                                            }
                                            if (drawingEmptyOr0)
                                            {
                                                drawingRevision = "";
                                            }

                                            if (partEmptyOr0 != drawingEmptyOr0 | partOrAssemblyRevision != drawingRevision)
                                            {
                                                integrationError = true;
                                                if (!string.IsNullOrEmpty(stringBuilder.ToString()))
                                                {
                                                    //Mellemrum
                                                    stringBuilder.AppendLine("");
                                                }
                                                stringBuilder.AppendLine($"State change blocked for {fileName}");
                                                stringBuilder.AppendLine($"Revision {drawingRevision} of drawing {result.Name} does not match revision {partOrAssemblyRevision} for {fileName}");
                                            }
                                        }

                                        result = search.GetNextResult();
                                    }
                                    else if (result.Name.ToUpper().Contains(".SLD")) { break; }
                                }

                                if (result != null)
                                {
                                    if (!setup.ignoreFilenameInStates.Contains(result.StateName))
                                    {
                                        integrationError = true;

                                        if (!string.IsNullOrEmpty(stringBuilder.ToString()))
                                        {
                                            //Mellemrum
                                            stringBuilder.AppendLine("");
                                        }
                                        stringBuilder.AppendLine($"State change blocked for {fileName}");
                                        stringBuilder.AppendLine($"{result.Name} exists in the state {result.StateName}");
                                    }
                                }
                            }
                            #endregion

                            #region PartNo & Number check

                            //var variable = file.GetVariableValue("Description", configurations[0]);

                            List<string> partNos = new List<string>();
                            List<string> numbers = new List<string>();

                            foreach (var item in configurations)
                            {
                                var tempVar = file.GetVariableFromDb("PartNo", item);
                                var tempVar2 = file.GetVariableFromDb("Number", item);
                                if (tempVar != null)
                                {
                                    partNos.Add(tempVar.ToString());
                                }
                                if (tempVar2 != null)
                                {
                                    numbers.Add(tempVar2.ToString());
                                }
                            }
                            //var partNo = file.GetVariableFromDb("PartNo", configurations);
                            //var number = file.GetVariableFromDb("Number", configurations);

                            bool correctNumber = false;
                            bool correctPartNo = false;

                            foreach (var partNo in partNos)
                            {
                                correctPartNo = partNo.ToLower() == filenameExType.ToLower();
                                if (!correctPartNo)
                                {
                                    integrationError = true;
                                    if (!string.IsNullOrEmpty(stringBuilder.ToString()))
                                    {
                                        //Mellemrum
                                        stringBuilder.AppendLine("");
                                    }
                                    stringBuilder.AppendLine($"State change blocked for {fileName}");
                                    stringBuilder.AppendLine($"PartNo is '{partNo}', but should be '{filenameExType}'");
                                    return;
                                }
                            }

                            if (numbers.Count != 0)
                            {
                                foreach (var number in numbers)
                                {
                                    correctNumber = number == null | number == "" | number.ToLower() == filenameExType.ToLower();
                                    if (!correctNumber)
                                    {
                                        integrationError = true;
                                        if (!string.IsNullOrEmpty(stringBuilder.ToString()))
                                        {
                                            //Mellemrum
                                            stringBuilder.AppendLine("");
                                        }
                                        stringBuilder.AppendLine($"State change blocked for {fileName}");
                                        stringBuilder.AppendLine($"Number is '{number}', but should be '{filenameExType}'");
                                        return;
                                    }
                                }
                            }
                            else
                            {
                                correctNumber = true;
                            }
                            #endregion
                        }
                    });

                    bool admin = false;

                    if (poCmd.meCmdType == EdmCmdType.EdmCmd_PreState)
                    {
                        admin = CheckIfAdmin(ppoData[0].mlObjectID4, vault);
                    }

                    if (integrationError & poCmd.meCmdType == EdmCmdType.EdmCmd_PreState)
                    {
                        if (!admin)
                        {
                            //Cancel state change for non-admins
                            poCmd.mbCancel = Convert.ToInt16(true);
                        }
                    }

                    if (string.IsNullOrEmpty(stringBuilder.ToString()) == false)
                    {
                        if (admin & poCmd.meCmdType == EdmCmdType.EdmCmd_PreState)
                        {
                            stringBuilder.AppendLine($"");
                            stringBuilder.AppendLine($"Do you still want to continue the state change?");
                            EdmMBoxResult result = Vault.MsgBox(WindowHandle, stringBuilder.ToString(), EdmMBoxType.EdmMbt_YesNo, "Warning");

                            if (result.HasFlag(EdmMBoxResult.EdmMbr_No))
                            {
                                poCmd.mbCancel = Convert.ToInt16(true);
                            }
                        }
                        else
                        {
                            Vault.MsgBox(WindowHandle, stringBuilder.ToString(), EdmMBoxType.EdmMbt_OKOnly, "Warning");
                        }
                    }
                    GC.Collect();
                }
                #endregion
            } catch (Exception e)
            {
                OnCreateErrorLog(e, null, WindowHandle, logFolder);
            }


            #region Add-in setup menu
            if (poCmd.mlCmdID == (int)Commands.SetupMenu)
            {
                var variables = Vault.GetVariableNames();

                var UserControl = new UserControl1() { workflows = Vault.GetWorkflows(), Vault = (IEdmVault7)Vault, dic = Vault.GetDictionary("PDM_Checker_Add_in", true), variables = variables };
                Window window = new Window
                {
                    Title = "Set up Add-in",
                    Content = UserControl,
                    Width = 900,
                    Height = 600,
                };

                window.Show();

                UserControl.LoadSetup();
            }
            #endregion

            #region Card button - Copy-paste to all configs
            if (poCmd.meCmdType == EdmCmdType.EdmCmd_CardButton)
            {
                dic.LongGetAt(1, out var pbsRetValue);
                setup = JsonConvert.DeserializeObject<Setup>(pbsRetValue);

                if (setup.enableCardButton == null | setup.enableCardButton == false) { return; }

                var btnName = poCmd.mbsComment;
                if (btnName == "CopyPasteToConfigurations")
                {
                    var activeConfig = ppoData[0].mbsStrData1;
                    IEdmFile17 file = Vault.GetFileFromPath(ppoData[0].mbsStrData2, out var parentFolder) as IEdmFile17;

                    //Give choice to user
                    EdmMBoxResult result = Vault.MsgBox(WindowHandle, "Are you sure you want to replace all data with this active config data?", EdmMBoxType.EdmMbt_YesNo);

                    if (result == EdmMBoxResult.EdmMbr_No)
                    {
                        return;
                    }

                    var configsArray = file.GetConfigurationNames();
                    var configs = configsArray.ToList();
                    configs.Add("@");

                    IEdmEnumeratorVariable5 vars = (IEdmEnumeratorVariable5)poCmd.mpoExtra;
                    IEdmStrLst5 ConfigNames = (IEdmStrLst5)((EdmCmdData)ppoData.GetValue(0)).mpoExtra;

                    foreach (var variable in setup.variables)
                    {
                        if (setup.skippedOnCopyVariables.Contains(variable))
                        {
                            //skip
                        }
                        else
                        {
                            object variableData;
                            vars.GetVar(variable, activeConfig, out variableData);
                            foreach (var config in configs)
                            {
                                if (config != activeConfig)
                                {
                                    try
                                    {
                                        vars.SetVar(variable, config, variableData, true);
                                    }
                                    catch (Exception ex)
                                    {
                                        Vault.MsgBox(WindowHandle, ex.Message, EdmMBoxType.EdmMbt_OKOnly);
                                    }
                                }
                            }
                        }
                    }
                    //Should be done according to documentation
                    vars.Flush();
                }
            }
            #endregion
        }

        private bool CheckIfAdmin(int userInt, IEdmVault21 vault)
        {
            IEdmUserMgr10 userMgr = default(IEdmUserMgr10);
            userMgr = (IEdmUserMgr10)vault;
            IEdmUser10 user = userMgr.GetUser(userInt) as IEdmUser10;
            user.GetGroupMemberships(out var groups);
            foreach (IEdmUserGroup8 group in groups)
            {
                if (group.Name == "Administrator")
                {
                    return true;
                }
            }
            return false;
        }

        protected override void OnLoggerTypeChosen(LoggerType_e defaultType)
        {
            base.OnLoggerTypeChosen(LoggerType_e.File);
        }

        protected override void OnRegisterAdditionalTypes(Container container)
        {
            // register types with the container 
        }

        protected override void OnLoggerOutputSat(string defaultDirectory)
        {
            // set the logger default directory - ONLY USE IF YOU ARE NOT LOGGING TO PDM
        }
        protected override void OnLoadAdditionalAssemblies(DirectoryInfo addinDirectory)
        {
            base.OnLoadAdditionalAssemblies(addinDirectory);
        }

        protected override void OnUnhandledExceptions(bool catchAllExceptions, Action<Exception> logAction = null)
        {
            this.CatchAllUnhandledException = false;

            logAction = (Exception e) =>
            {
                //throw new 
            };


            base.OnUnhandledExceptions(catchAllExceptions, logAction);
        }

        private void OnCreateErrorLog(Exception error, IEdmFile5 file, int prntWnd, IEdmFolder5 logFolder)
        {
            string newFile = Guid.NewGuid().ToString() + "-" + DateTime.Now.ToShortDateString() + ".txt";

            string newFilePath = System.IO.Path.Combine(logFolder.LocalPath, newFile);

            using (StreamWriter sw = File.CreateText(newFilePath))
            {
                if (file != null)
                {
                    sw.WriteLine(file.Name);
                }

                sw.WriteLine(error.Message);
                sw.WriteLine(error.InnerException);
                sw.WriteLine(error.TargetSite);
                sw.WriteLine(error.StackTrace);
            }

            var id = logFolder.AddFile(prntWnd, newFilePath);

            var logFile = Vault.GetObject(EdmObjectType.EdmObject_File, id) as IEdmFile5;

            logFile.UnlockFile(prntWnd, "Added by PDM Add-in");
        }

        private void ConditionChecks(VariableRules rule, Dictionary<string, object> variables, string fileName, string filenameExType, ref bool integrationError, StringBuilder stringBuilder, string config)
        {
            variables.TryGetValue(rule.variable, out var variableValue);
            bool isNull = false;
            switch (rule.condition)
            {
                case ConditionRule.FileName:
                    isNull = string.IsNullOrEmpty((string)variableValue);
                    if (isNull)
                    {
                        integrationError = true;
                        stringBuilder.AppendLine($"{fileName}-{config}: {EvaluateString(rule.message, variables)}");
                        break;
                    }

                    bool isCorrect = variableValue.ToString().ToUpper() == filenameExType.ToUpper();
                    if (!isCorrect)
                    {
                        integrationError = true;
                        stringBuilder.AppendLine($"{fileName}-{config}: {EvaluateString(rule.message, variables)}");
                    }
                    break;

                case ConditionRule.FileNameOrEmpty:
                    isNull = string.IsNullOrEmpty((string)variableValue);
                    if (isNull)
                    {
                        break;
                    }

                    bool isFileName = variableValue.ToString().ToUpper() == filenameExType.ToUpper();
                    if (!isFileName)
                    {
                        integrationError = true;
                        stringBuilder.AppendLine($"{fileName}-{config}: {EvaluateString(rule.message, variables)}");
                    }
                    break;

                case ConditionRule.TextContain:
                    isNull = string.IsNullOrEmpty((string)variableValue);
                    if (isNull)
                    {
                        integrationError = true;
                        stringBuilder.AppendLine($"{fileName}-{config}: {EvaluateString(rule.message, variables)}");
                        break;
                    }

                    bool textContains = variableValue.ToString().ToLower().Contains(rule.ruleText.ToLower());
                    if (!textContains)
                    {
                        integrationError = true;
                        stringBuilder.AppendLine($"{fileName}-{config}: {EvaluateString(rule.message, variables)}");
                    }
                    break;

                case ConditionRule.TextDoesntContain:
                    isNull = string.IsNullOrEmpty((string)variableValue);
                    if (isNull)
                    {
                        integrationError = true;
                        stringBuilder.AppendLine($"{fileName}-{config}: {rule.message}");
                        break;
                    }

                    bool textDoesntContains = !variableValue.ToString().ToLower().Contains(rule.ruleText.ToLower());
                    if (!textDoesntContains)
                    {
                        integrationError = true;
                        stringBuilder.AppendLine($"{fileName}-{config}: {EvaluateString(rule.message, variables)}");
                    }
                    break;

                case ConditionRule.TextEqual:
                    isNull = string.IsNullOrEmpty((string)variableValue);
                    if (isNull)
                    {
                        variableValue = "";
                    }

                    bool textEquals = variableValue.ToString().ToLower() == rule.ruleText.ToLower();
                    if (!textEquals)
                    {
                        integrationError = true;
                        stringBuilder.AppendLine($"{fileName}-{config}: {EvaluateString(rule.message, variables)}");
                    }
                    break;

                case ConditionRule.TextNotEqual:
                    isNull = string.IsNullOrEmpty((string)variableValue);
                    if (isNull)
                    {
                        variableValue = "";
                    }

                    bool textNotEquals = variableValue.ToString().ToLower() != rule.ruleText.ToLower();
                    if (!textNotEquals)
                    {
                        integrationError = true;
                        stringBuilder.AppendLine($"{fileName}-{config}: {EvaluateString(rule.message, variables)}");
                    }
                    break;

                case ConditionRule.NotEmpty:
                    bool isEmpty = string.IsNullOrEmpty((string)variableValue);
                    if (isEmpty)
                    {
                        integrationError = true;
                        stringBuilder.AppendLine($"{fileName}-{config}: {EvaluateString(rule.message, variables)}");
                    }
                    break;

                case ConditionRule.Variable:

                    isNull = string.IsNullOrEmpty((string)variableValue);
                    if (isNull)
                    {
                        integrationError = true;
                        stringBuilder.AppendLine($"{fileName}-{config}: {EvaluateString(rule.message, variables)}");
                        break;
                    }

                    break;

                case ConditionRule.Regex:

                    isNull = string.IsNullOrEmpty((string)variableValue);
                    if (isNull)
                    {
                        variableValue = "";
                    }

                    bool isMatch = UseRegex(rule.ruleText, variableValue);
                    if(!isMatch)
                    {
                        integrationError = true;
                        stringBuilder.AppendLine($"{fileName}-{config}: {EvaluateString(rule.message, variables)}");
                        break;
                    }

                    break;
            }
        }

        private bool UseRegex(string regexString, object input)
        {
            Regex regex = new Regex(regexString);

            Match match = regex.Match((string)input);
            
            return match.Success;
        }

        public string EvaluateString(string input, Dictionary<string, object> variableDictionary)
        {
            // Regular expression to match expressions like {variableName}
            Regex regex = new Regex(@"\{([^{}]*)\}");

            // Replace each matched expression with its corresponding value from the dictionary
            string output = regex.Replace(input, match =>
            {
                string variableName = match.Groups[1].Value;
                if (variableDictionary.ContainsKey(variableName))
                {
                    return (string)variableDictionary[variableName];
                }
                // If the variable is not found, keep the original expression
                return match.Value;
            });

            return output;
        }
    }
}