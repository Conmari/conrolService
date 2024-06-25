using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.ServiceProcess;
using System;
using System.Text.RegularExpressions;
using System.Management;

using static closeServ.MainWindow;
using Microsoft.Win32;

namespace closeServ
{

    public partial class MainWindow : Window
    {

        public const string autoTime = "Автоматически (отложенный запуск)";
        public const string auto = "Автоматически";
        public const string manual = "Ручной запуск";
        public const string disabled = "Отключен";
        public const string none = "не делать";

        public class ServiceInfo
        {
            public string ServiceName { get; set; }
            public string DisplayName { get; set; }

            public override bool Equals(object obj)
            {
                if (obj == null || GetType() != obj.GetType())
                {
                    return false;
                }

                ServiceInfo other = (ServiceInfo)obj;
                return ServiceName == other.ServiceName && DisplayName == other.DisplayName;
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(ServiceName, DisplayName);
            }
        }

        ServiceController[] services = ServiceController.GetServices();

        List<ServiceInfo> serviceInfos = new List<ServiceInfo>();
        public MainWindow()
        {
            InitializeComponent();
            typeStart.ItemsSource = new List<string>()
            {
                none,
                auto,
                manual,
                disabled,
                autoTime
            };
            s_displayName.IsChecked = true;
        }

        // Завершение службы
        private void TerminatService(object sender, RoutedEventArgs e)
        {
            // Перебор всех служб
            foreach (ServiceInfo selectedItem in SelectedService.SelectedItems)
            {
                ServiceInfo clonedItem = new ServiceInfo { ServiceName = selectedItem.ServiceName, DisplayName = selectedItem.DisplayName };
                // Проверяем, является ли служба PostgreSQL
                if (clonedItem.ServiceName.ToLower().Contains("postgresql"))
                {
                    // Поиск ключа реестра, содержащего "postgresql"
                    string baseRegistryKey = @"SOFTWARE\PostgreSQL\Installations";
                    string registryKeyPath = null;
                    using (RegistryKey baseKey = Registry.LocalMachine.OpenSubKey(baseRegistryKey))
                    {
                        if (baseKey != null)
                        {
                            registryKeyPath = baseKey.GetSubKeyNames()
                                .FirstOrDefault(subKeyName => subKeyName.ToLower().Contains("postgresql"));

                            if (registryKeyPath != null)
                            {
                                registryKeyPath = $@"{baseRegistryKey}\{registryKeyPath}";
                            }
                        }
                    }

                    // Если ключ найден, получаем путь к директории данных и останавливаем PostgreSQL
                    if (registryKeyPath != null)
                    {
                        string dataDirectory = "";
                        using (RegistryKey key = Registry.LocalMachine.OpenSubKey(registryKeyPath))
                        {
                            if (key != null)
                            {
                                Object o = key.GetValue("Data Directory");
                                if (o != null)
                                {
                                    dataDirectory = o.ToString();
                                }
                            }
                        }

                        // Если путь найден, останавливаем PostgreSQL
                        if (!string.IsNullOrEmpty(dataDirectory))
                        {
                            string command = $"pg_ctl -D \"{dataDirectory}\" stop";
                            // Запуск команды с правами администратора
                            ProcessStartInfo procStartInfo = new ProcessStartInfo("cmd", $"/c {command}")
                            {
                                UseShellExecute = true,
                                Verb = "runas"
                            };

                            try
                            {
                                Process proc = Process.Start(procStartInfo);
                                proc.WaitForExit();
                            }
                            catch (System.ComponentModel.Win32Exception)
                            {
                                MessageBox.Show("Операция была отменена пользователем или требуются права администратора.");
                            }
                        }
                    }
                }
                // Все остальные службы
                else
                {
                    Process.Start("cmd", $"/c sc stop {clonedItem.ServiceName}").WaitForExit();
                }
            }

        }

        // Запуск службы
        private void StartService(object sender, RoutedEventArgs e)
        {
            foreach (ServiceInfo selectedItem in SelectedService.SelectedItems)
            {
                TypeStartServ(selectedItem);
                ServiceInfo clonedItem = new ServiceInfo { ServiceName = selectedItem.ServiceName, DisplayName = selectedItem.DisplayName };

                ProcessStartInfo procStartInfo = new ProcessStartInfo("cmd", $"/c sc start {clonedItem.ServiceName.ToString()}")
                {
                    UseShellExecute = true,
                    Verb = "runas" // Запуск от имени администратора
                };

                try
                {
                    Process proc = Process.Start(procStartInfo);
                    proc.WaitForExit(); // Ожидаем завершения процесса
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Обработка исключения, если пользователь не предоставил права администратора
                    MessageBox.Show("Операция была отменена пользователем или требуются права администратора.");
                }

            }
        }

        // Все служб
        private void AllServicePС(object sender, RoutedEventArgs e)
        {
            //ServiceController[] services = ServiceController.GetServices();
            foreach (ServiceController service in services)
            {

                serviceInfos.Add(new ServiceInfo { ServiceName = service.ServiceName, DisplayName = service.DisplayName });
            }
            // Устанавливаем список объектов ServiceInfo как источник данных для AllService
            AllService.ItemsSource = serviceInfos;
        }

        // Добавление в лист "SelectedService" Выбранных служб
        private void MoveSelectElement(object sender, RoutedEventArgs e)
        {
            MoveSelectedItems();
        }

        // поиск служб
        private void SearchService(object sender, RoutedEventArgs e)
        {
            //SelectedService.Items.Clear();
            HashSet<ServiceInfo> uniqueItems = new HashSet<ServiceInfo>();
            foreach (ServiceInfo serviceInfo in AllService.Items)
            {
                Regex regex = new Regex(@$"{searchBox.Text.ToLower()}(\w*)");
                MatchCollection matches = null;

                if (s_displayName.IsChecked == true) {matches = regex.Matches(serviceInfo.DisplayName.ToLower());}
                if (s_ServiceName.IsChecked == true) {matches = regex.Matches(serviceInfo.ServiceName.ToLower());}               
                
                foreach (Match match in matches)
                {
                    ServiceInfo clonedItem = new ServiceInfo { ServiceName = serviceInfo.ServiceName, DisplayName = serviceInfo.DisplayName };

                    if (!uniqueItems.Contains(clonedItem))
                    {
                        SelectedService.Items.Add(clonedItem);
                        uniqueItems.Add(clonedItem); 
                    }                    
                }
            }
        }
        // функция для переноса выбранных элементов в другой список
        private void MoveSelectedItems()
        {
            foreach (ServiceInfo selectedItem in AllService.SelectedItems)
            {
                bool itemExists = false;
                // Проверка на существование такого элемента в другом списке 
                foreach (ServiceInfo item in SelectedService.Items)
                {
                    if (item.ServiceName == selectedItem.ServiceName && item.DisplayName == selectedItem.DisplayName)
                    {
                        itemExists = true;
                        break;
                    }
                }
                if (!itemExists)
                {
                    // Клонируем выбранный элемент, чтобы избежать проблем с привязкой
                    ServiceInfo clonedItem = new ServiceInfo { ServiceName = selectedItem.ServiceName, DisplayName = selectedItem.DisplayName };
                    SelectedService.Items.Add(clonedItem);
                }
            }
        }
        // в службых вкладка - востанавление будет применён тип "перезапуск службы" у выбранных задач
        private void ApplyServiceTypeStartup(object sender, RoutedEventArgs e)
        {
            foreach (ServiceInfo selectedItem in SelectedService.SelectedItems)
            {
                ServiceInfo clonedItem = new ServiceInfo { ServiceName = selectedItem.ServiceName, DisplayName = selectedItem.DisplayName };
                Process.Start("cmd", $"/c sc failure {clonedItem.ServiceName.ToString()} reset= 66400 actions= restart/60000/restart/60000/restart/60000").WaitForExit();

            }
        }
        // Для выбора типа запуска службы
        private void TypeStartServ(ServiceInfo selectedItem)
        {
            ServiceInfo clonedItem = new ServiceInfo { ServiceName = selectedItem.ServiceName, DisplayName = selectedItem.DisplayName };
            switch (typeStart.SelectedValue)
            {
                case autoTime:
                    Process.Start("cmd", $"/c sc config {clonedItem.ServiceName.ToString()} start=delayed-auto").WaitForExit();
                    break;
                case auto:
                    Process.Start("cmd", $"/c sc config {clonedItem.ServiceName.ToString()} start=auto").WaitForExit();
                    break;
                case manual:
                    Process.Start("cmd", $"/c sc config {clonedItem.ServiceName.ToString()} start=demand").WaitForExit();
                    break;
                case disabled:
                    Process.Start("cmd", $"/c sc config {clonedItem.ServiceName.ToString()} start=disabled").WaitForExit();
                    break;
                case none:
                    break;
                default:
                    break;
            }
        }
        // служба от другого пользователя
        private void ServiceAnotherUser(object sender, RoutedEventArgs e)
        {
            foreach (ServiceInfo selectedItemService in SelectedService.SelectedItems)
            {
                Process.Start("cmd", $"/с sc config {selectedItemService.ServiceName} obj= \".\\{login.Text}\" password= \"{password.Text}\"").WaitForExit();
            }
        }
        // Принудительное завершение через Taskkill
        private void ForcedKillProcess(object sender, RoutedEventArgs e)
        {
            foreach (ServiceInfo selectedItemService in SelectedService.SelectedItems)
            {
                ServiceInfo clonedItem = new ServiceInfo { ServiceName = selectedItemService.ServiceName, DisplayName = selectedItemService.DisplayName };
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Service WHERE Name = '" + clonedItem.ServiceName + "'");

                foreach (ManagementObject service in searcher.Get())
                {
                    string processId = service["ProcessId"].ToString();
                    //MessageBox.Show($"PID процесса для службы {selectedItemService.ServiceName}: {processId}");
                    Process.Start("cmd", $"/c TASKKILL/PID {processId} -F").WaitForExit();
                }
            }
        }
        // Поиск по ServiceName при активном
        private void CheckBoxServiceNameON (object sender, RoutedEventArgs e)
        {
            if(s_ServiceName.IsChecked == true)
            {
                s_displayName.IsChecked = false;
            }
        }
        // Поиск по ServiceName при неактивном
        private void CheckBoxServiceNameOFF(object sender, RoutedEventArgs e)
        {
            if (s_ServiceName.IsChecked == false)
            {
                s_displayName.IsChecked = true;
            }
        }
        // Поиск по DisplayName при активном
        private void CheckBoxDisplayNameON(object sender, RoutedEventArgs e)
        {
            if(s_displayName.IsChecked == true)
            {
                s_ServiceName.IsChecked = false;
            }
        }
        // Поиск по DisplayName при активном
        private void CheckBoxDisplayNameOFF(object sender, RoutedEventArgs e)
        {
            if (s_displayName.IsChecked == false)
            {
                s_ServiceName.IsChecked = true;
            }
        }
        // Очистить весь список отображённых служб
        private void Btn_deletedAllService(object sender, RoutedEventArgs e)
        {
            SelectedService.Items.Clear();
        }

        // Очистить весь список отображённых служб
        private void Btn_DeletedSelectedService(object sender, RoutedEventArgs e)
        {
            foreach (var item in SelectedService.SelectedItems.Cast<object>().ToList())
            {
                SelectedService.Items.Remove(item);
            }
        }
    }
}