using System;
using System.Collections.Generic;
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
using System.IO;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.Configuration;
using System.Threading;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Events;
using System.Windows.Threading;
using System.Diagnostics;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;


namespace VideoConverter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        string? path;
        List<string>? files;
        int totalCount;
        double increment;
        int curent = 0;
        long TotalTime = 0;

        //Настройки системы
        bool ignoreMP4 = true;
        bool useNvidiaAcseliration = false;

        public MainWindow()
        {
            InitializeComponent();
            try
            {
                FFmpeg.SetExecutablesPath("C:\\Program Files\\FFmpeg\\bin");//Указываем путь до ffmpeg, мало ли
                System.Environment.SetEnvironmentVariable("CUDA_VISIBLE_DEVICES", "0");//Это, чтобы можно было пользоваться ускорением через GPU
                //PrepareAndStart();
            }
            catch(Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка!", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void PrepareAndStart()
        {
            path = SelectFolder();
            while (String.IsNullOrEmpty(path))
            {
                var res = MessageBox.Show($"Путь не выбран, или выбран некорректно. Попробовать снова?", "Неверный путь",
                    MessageBoxButton.YesNo, MessageBoxImage.Error);
                if (res == MessageBoxResult.No) Application.Current.Shutdown();

                path = SelectFolder();
            }
            LoadDataFromDir();
            //По итогу, тут гарантированно в path будет путь, либо прогу просто закроют...
            await StartConvertation();
            TotalProgressBar.Value = 0;//Чтобы смотрелось красивее
            CurrentProgresBar.Value = 0;//убераем прогресс в 0
            MessageBox.Show(
                $"Конвертация успешно завершена.\nКол-во файлов:{totalCount}\nСреднее время на конвертацию одного файла:{TotalTime*1f/totalCount/1000/60} мин.\nОбщее время:{TotalTime*1f/1000/60} мин.",
                "Успешное завершение.",MessageBoxButton.OK,MessageBoxImage.Information); 
        }
        
        private void LoadDataFromDir()
        {
            files = Directory.GetFiles(path).Where(x => !ignoreMP4 || System.IO.Path.GetExtension(x) != ".mp4").ToList();//Исходя из логики path тут не может быть null + mp4 файлы конвертировать безсмысленно
            totalCount = files.Count;

            PathText.Text = path;
            TotalCountText.Text = $"{totalCount} шт.";
        }

        private async Task StartConvertation()
        {

            increment = 1f / totalCount;
            curent = 0;
            TotalTime = 0;
            Stopwatch sw = new();
            TotalProgressBar.Value = 0;
            FileProgresTextRun.Text = $"0/{totalCount}";


            foreach (var item in files)
            {
                sw.Restart();
                //Тут мы мерием время 1-ой конвертации, чтобы расчитать среднее
                await Convert(item);
                sw.Stop();

                TotalTime += sw.ElapsedMilliseconds; //Сохраняем прошлое время конвертации
                curent++;//Мы сконвертировали 1 файл
                CurrentProgresBar.Value = 0;//Полоску прогресс бара в 0. Не обязательно, но почему бы и нет.
                UpdateUderInfo();
            }
        }

        public void UpdateUderInfo()
        {
            TotalProgressBar.Value = increment * curent;
            SingleConvertationText.Text = $"{TotalTime * 1f / curent / 1000} с.";
            RemainingTimeText.Text = $"{(totalCount - curent)*(TotalTime * 1f / curent) /1000 /60 } мин.";
            FileProgresTextRun.Text = $"{curent}/{totalCount}";
        }

        public void OnProgres(object sender, ConversionProgressEventArgs args)
        {
            double tmp = args.Duration.TotalSeconds / args.TotalLength.TotalSeconds;//Экономим оепрации и делим всего 1 раз
            CurrentProgresBar.Value = tmp;
            TotalProgressBar.Value = (curent + tmp) * increment;//Это для того, чтобы столбик общего прогресса двиался плавно, а не скокал от curenta сразу на +1
        }

        private async Task Convert(string path)
        {
            //Способ получить новое название файла, равное старому, но с другим расширением.
            string extention = System.IO.Path.GetExtension(path);
            string fileEnd = ignoreMP4 ? "" : "_";
            var output = $"{path.Substring(0, path.Length - extention.Length)}{fileEnd}.mp4";

            var mediaInfo = await FFmpeg.GetMediaInfo(path);

            var conversion = FFmpeg.Conversions.New().AddStream(mediaInfo.Streams)
                            .AddParameter("-threads 16")//Используем все потоки, что есть
                            .AddParameter("-hide_banner")//Прячем баннер, при ошибках полезно
                            .AddParameter("-ac 2");//Это для решения багули с отсутствием звука при конвертации из 5.1 звука
                                                   
            if (useNvidiaAcseliration)
            {
                //Это для аппаратного успорения на видеокарте. !!! обязательно нужно выставить переменную под свой компьютер(см. выше)
                conversion = conversion.AddParameter("-c:v h264_nvenc");
            }

            conversion = conversion.SetOutput(output).SetOutputFormat(Format.mp4);

            conversion.OnProgress += (sender, args) => Dispatcher.Invoke(new System.Action(() => {
               OnProgres(sender, args);
            }));
            try
            {
                //Устанавливаем метод, что отражает прогресс. Подобная странноватая конструкция необходима для согласования потоков, т. к. прогресс бар доступен только из главного...
                await conversion.Start();//Поехали
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка!", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string? SelectFolder()
        {
            var dialog = new CommonOpenFileDialog();
            dialog.IsFolderPicker = true;//Мы выбираем только папки, это важно
            CommonFileDialogResult result = dialog.ShowDialog();
            if (result == CommonFileDialogResult.Ok) return dialog.FileName;
            return null;
        }

        private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
        {
            path = SelectFolder();
            if (string.IsNullOrEmpty(path)) return;
            LoadDataFromDir();
        }

        private async void StartConvertationButton_Click(object sender, RoutedEventArgs e)
        {
            if(string.IsNullOrEmpty(path) || files == null || files.Count == 0)
            {
                MessageBox.Show("Сначала необходимо выбрать подходящую папку!","Конвертация невозможна",MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            await StartConvertation();
            TotalProgressBar.Value = 0;//Чтобы смотрелось красивее
            CurrentProgresBar.Value = 0;//убераем прогресс в 0
            MessageBox.Show(
                $"Конвертация успешно завершена.\nКол-во файлов:{totalCount}\nСреднее время на конвертацию одного файла:{TotalTime * 1f / totalCount / 1000 / 60} мин.\nОбщее время:{TotalTime * 1f / 1000 / 60} мин.",
                "Успешное завершение.", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void IgnoreMP4MenuItem_Click(object sender, RoutedEventArgs e)
        {
            ignoreMP4 = IgnoreMP4MenuItem.IsChecked;
            if (!string.IsNullOrEmpty(path)) 
            {
                LoadDataFromDir();
            }
        }

        private void UseNvidiaGPUMenuItem_Click(object sender, RoutedEventArgs e)
        {
            useNvidiaAcseliration = UseNvidiaGPUMenuItem.IsChecked;
        }

        private async void TestConvertationMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(path) || files == null || files.Count == 0)
            {
                MessageBox.Show("Сначала необходимо выбрать подходящую папку!", "Конвертация невозможна", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (MessageBox.Show("Запустить тестовую конвертацию?", "Запрос", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            await Convert(files.First());
            TotalProgressBar.Value = 0;//Чтобы смотрелось красивее
            CurrentProgresBar.Value = 0;//убераем прогресс в 0
            MessageBox.Show(
                $"Конвертация успешно завершена.\nКол-во файлов:{totalCount}\nСреднее время на конвертацию одного файла:{TotalTime * 1f / totalCount / 1000 / 60} мин.\nОбщее время:{TotalTime * 1f / 1000 / 60} мин.",
                "Успешное завершение.", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
