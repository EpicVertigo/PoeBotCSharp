using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace PoeBotCSharp
{
    public partial class MainWindow : Window
    {
        private const String APP_ID = "Path of Exile Chat Monitor";
        public static string LAST_NICKNAME = "";
        public static string LAST_MESSAGE = "";
        System.Text.RegularExpressions.Regex UNHUMAN_REGEX = new System.Text.RegularExpressions.Regex(@"@From\s(?:(?<sas>[^<]\w*)|<.*>\s(?<sas>\w*)):\s(?<text>.*)");
        public static int MESSAGES_COUNT = 0;
        bool taskBool = false;
        public static Settings settings = new Settings();
        Task mainLoop;
        public static XmlDocument toastXml;
        public static ToastNotification toast;

        public MainWindow()
        {
            OpenSettings();
            InitializeComponent();
            pathTextBox.Text = settings.path;
            keywordsTextBox.Text = string.Join(", ", settings.keywords);
            minimizeCheckbox.IsChecked = settings.minimize;
            tbi.Visibility = Visibility.Hidden;
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized && settings.minimize == true) 
            {
                LeMainWindow.Hide();
                tbi.Visibility = Visibility.Visible;
            }
        }

        #region Win10 Toast

        public void CreateToast(string nickname, string message)
        {
            //Get a toast XML template
            XmlDocument toastXml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastImageAndText04);

            // Fill in the text elements
            XmlNodeList stringElements = toastXml.GetElementsByTagName("text");

            stringElements[1].AppendChild(toastXml.CreateTextNode(nickname));
            stringElements[2].AppendChild(toastXml.CreateTextNode(message));

            // Specify the absolute path to an image
            String imagePath = Path.Combine(Directory.GetCurrentDirectory(), "Images/toastImageAndText.png");
            XmlNodeList imageElements = toastXml.GetElementsByTagName("image");
            imageElements[0].Attributes.GetNamedItem("src").NodeValue = imagePath;

            // Create the toast and attach event listeners
            ToastNotification toast = new ToastNotification(toastXml);
            toast.Activated += ToastActivated;

            ToastNotificationManager.CreateToastNotifier(APP_ID).Show(toast);
        }

        private void ToastActivated (ToastNotification sender, object e)
        {
            Dispatcher.Invoke(() =>
            {
                LeMainWindow.Show();
                LeMainWindow.WindowState = WindowState.Normal;
                LeMainWindow.Activate();
            });
        }

        #endregion

        #region Chat Monitor

        public void tail()
        {
            taskBool = true;
            string lastLine;
            var helper = new FlashWindowHelper(Application.Current);

            try
            {
                FileStream fs = new FileStream(settings.path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                StreamReader sr = new StreamReader(fs);
                sr.BaseStream.Seek(0, SeekOrigin.End);

                while (taskBool)
                {
                    try
                    {
                        lastLine = sr.ReadLine();

                        if (lastLine == null || lastLine == "")
                        {
                            Task.Delay(1000);
                            continue;
                        }
                        else if (lastLine.Contains("] @From") || settings.keywords.Any(lastLine.ToUpper().Contains))
                        {
                            MESSAGES_COUNT++;
                            var count = settings.keywords.Count();
                            var superlist = settings.keywords;

                            lastLine = lastLine.Split(new string[] { "] " }, StringSplitOptions.None)[1];
                            ChatBlock.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(() =>
                            {
                                var lineRegex   = UNHUMAN_REGEX.Match(lastLine);
                                LAST_NICKNAME   = lineRegex.Groups["sas"].ToString();
                                LAST_MESSAGE    = lineRegex.Groups["text"].ToString();
                                ChatBlock.AppendText(lastLine + "\n");
                                MainScroll.ScrollToEnd();
                                helper.FlashApplicationWindow();
                            }
                                   ));
                            MessagesCountLabel.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(() =>
                            {
                                MessagesCountLabel.Text = MESSAGES_COUNT.ToString();
                            }
                                   ));
                            CreateToast(LAST_NICKNAME, LAST_MESSAGE);
                        }
                    }
                    catch (System.IndexOutOfRangeException)
                    {
                        ChatBlock.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(() =>
                        {
                            TextRange tr = new TextRange(ChatBlock.Document.ContentEnd, ChatBlock.Document.ContentEnd)
                            {
                                Text = "Wrong data range \n"
                            };
                            tr.ApplyPropertyValue(TextElement.ForegroundProperty, Brushes.Red);
                            helper.FlashApplicationWindow();
                        }
                                   ));
                        continue;
                    }
                }
            }
            catch (FileNotFoundException)
            {
                MessageBox.Show("Please choose proper client.txt file", "File not found", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion

        #region Chat Window Buttons
        private void btnStartAction(object sender, RoutedEventArgs e)
        {
            if (!taskBool)
            {
                mainLoop = new Task(tail, TaskCreationOptions.LongRunning);
                mainLoop.Start();
                BtnStop.IsEnabled = true;
                BtnStart.IsEnabled = false;
            }
        }

        private void btnStopAction(object sender, RoutedEventArgs e)
        {
            taskBool = false;
            BtnStart.IsEnabled = true;
            BtnStop.IsEnabled = false;
        }

        private void btnClearLog(object sender, RoutedEventArgs e)
        {
            ChatBlock.Document.Blocks.Clear();
            MessagesCountLabel.Text = "0";
            LAST_NICKNAME = "";
            MESSAGES_COUNT = 0;
        }
        #endregion

        #region Settings
        static void SaveSettings(object settings)
        {
            string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            Console.WriteLine(json);
            using (StreamWriter sw = new StreamWriter("settings.json"))
            {
                sw.WriteLine(json);
            }
        }
        static void CreateSettingsFile()
        {
            settings.path = "Default path";
            settings.keywords = new List<string> { "EXAMPLE", "EXAMPLE" };
            SaveSettings(settings);
        }
        static void OpenSettings()
        {
            try
            {
                using (StreamReader settingsFile = File.OpenText("settings.json"))
                {
                    JsonSerializer serializer = new JsonSerializer();
                    settings = (Settings)serializer.Deserialize(settingsFile, typeof(Settings));
                }
            }
            catch (FileNotFoundException)
            {
                MessageBox.Show("Error parsing settings file. Creating a new one..", "Settings file is missing or corrupted", MessageBoxButton.OK, MessageBoxImage.Error);
                CreateSettingsFile();
            }
            catch (Newtonsoft.Json.JsonReaderException)
            {
                MessageBox.Show("Error parsing settings file. Creating a new one..", "Settings file is missing or corrupted", MessageBoxButton.OK, MessageBoxImage.Error);
                CreateSettingsFile();
            }
        }
        #endregion

        #region File Opener
        private void btnOpenFile(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";

            var tempPath = settings.path;
            var tempPathTextBox = pathTextBox.Text;

            if (openFileDialog.ShowDialog() == true)
            {
                pathTextBox.Text = openFileDialog.FileName;
                settings.path = openFileDialog.FileName;
                SaveSettings(settings);

                if (mainLoop != null)
                {
                    RestartMainTask();
                }
            }
            else
            {
                settings.path = tempPath;
                pathTextBox.Text = tempPathTextBox;
            }
        }
        #endregion

        #region Keywords Field
        private void btnReloadKeywords(object sender, RoutedEventArgs e)
        {
            GetKeywordsList();
        }

        private void keywordsTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                GetKeywordsList();
            }
        }

        public void GetKeywordsList()
        {
            var tempKeywordsList = keywordsTextBox.Text.ToUpper().Split(',').ToList();

            for (int i = 0; i < tempKeywordsList.Count(); i++)
            {
                tempKeywordsList[i] = tempKeywordsList[i].Trim();
            }

            settings.keywords = tempKeywordsList;
            keywordsTextBox.Text = string.Join(", ", settings.keywords);

            SaveSettings(settings);

            if (mainLoop != null)
            {
                RestartMainTask();
            }

        }
        #endregion

        #region Tray Context Menu
        private void TrayMenu_ShowChat(object sender, RoutedEventArgs e)
        {
            LeMainWindow.Show();
            LeMainWindow.WindowState = WindowState.Normal;
            tbi.Visibility = Visibility.Hidden;
        }

        private void TrayMenu_CopyLast(object sender, RoutedEventArgs e)
        {
            if (LAST_NICKNAME != null && LAST_NICKNAME != "")
            {
                Clipboard.SetText("@" + LAST_NICKNAME);
            }
        }

        private void TrayMenu_Exit(object sender, RoutedEventArgs e)
        {
            tbi.Visibility = Visibility.Hidden;
            Environment.Exit(1);
        }
        #endregion

        #region Tray Checkbox
        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            settings.minimize = true;
            SaveSettings(settings);
        }

        private void CheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            settings.minimize = false;
            SaveSettings(settings);

            if (tbi.Visibility == Visibility.Visible) tbi.Visibility = Visibility.Hidden;
        }
        #endregion

        #region Help buttons
        private void helpPathClick(object sender, RoutedEventArgs e)
        {
            pathToolTip.IsOpen = !pathToolTip.IsOpen;
        }

        private void helpPath_MouseLeave(object sender, MouseEventArgs e)
        {
            if (pathToolTip.IsOpen == true) pathToolTip.IsOpen = false;
        }

        private void helpKeywordsClick(object sender, RoutedEventArgs e)
        {
            keywordsToolTip.IsOpen = !keywordsToolTip.IsOpen;
        }

        private void helpKeywords_MouseLeave(object sender, MouseEventArgs e)
        {
            if (keywordsToolTip.IsOpen == true) keywordsToolTip.IsOpen = false;
        }
        #endregion

        public void RestartMainTask()
        {
            taskBool = false;
            mainLoop = new Task(tail, TaskCreationOptions.LongRunning);
            mainLoop.Start();
        }

        private void CopyNickname_Click(object sender, RoutedEventArgs e)
        {
            if (LAST_NICKNAME != null && LAST_NICKNAME != "")
            {
                Clipboard.SetText("@"+LAST_NICKNAME);
            }
        }
    }
}
