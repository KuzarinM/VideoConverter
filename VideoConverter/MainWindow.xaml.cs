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
using Xabe.FFmpeg;
using System.IO;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.Configuration;
using System.Threading;
using Xabe.FFmpeg.Events;
using System.Windows.Threading;
using System.Diagnostics;

namespace VideoConverter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        string? path;
        int totalCount;
        double increment;
        int curent = 0;
        long TotalTime = 0;

        public MainWindow()
        {
            InitializeComponent();
            FFmpeg.SetExecutablesPath("C:\\Program Files\\FFmpeg\\bin");//Указываем путь до ffmpeg, мало ли
            System.Environment.SetEnvironmentVariable("CUDA_VISIBLE_DEVICES", "0");//Это, чтобы можно было пользоваться ускорением через GPU
            PrepareAndStart();
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
            //По итогу, тут гарантированно в path будет путь, либо прогу просто закроют...
            await StartConvertation();
            TotalProgressBar.Value = 0;//Чтобы смотрелось красивее
            CurrentProgresBar.Value = 0;//убераем прогресс в 0
            MessageBox.Show(
                $"Конвертация успешно завершена.\nКол-во файлов:{totalCount}\nСреднее время на конвертацию одного файла:{TotalTime*1f/totalCount/1000/60} мин.\nОбщее время:{TotalTime*1f/1000/60} мин.",
                "Успешное завершение.",MessageBoxButton.OK,MessageBoxImage.Information); 
        }

        private async Task StartConvertation()
        {
            var files = Directory.GetFiles(path);//Исходя из логики path тут не может быть null
            totalCount = files.Length;
            increment = 1f / totalCount;
            Stopwatch sw = new();

            PathText.Text = path;
            TotalCountText.Text = $"{totalCount} шт.";
            TotalProgressBar.Value = 0;
            
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
            var output = $"{path.Substring(0, path.Length - extention.Length)}.mp4";

            var mediaInfo = await FFmpeg.GetMediaInfo(path);
            var conversion = FFmpeg.Conversions.New().AddStream(mediaInfo.Streams)
                            .AddParameter("-threads 16")//Используем все потоки, что есть
                            .AddParameter("-hide_banner")//Прячем баннер, при ошибках полезно
                            .AddParameter("-ac 2")//Это для решения багули с отсутствием звука при конвертации из 5.1 звука
                            .AddParameter("-c:v h264_nvenc")//Это для аппаратного успорения на видеокарте. !!! обязательно нужно выставить переменную под свой компьютер(см. выше)
                            .SetOutput(output).SetOutputFormat(Format.mp4);

            conversion.OnProgress += (sender, args) => Dispatcher.Invoke(new System.Action(() => {
               OnProgres(sender, args);
            }));
            //Устанавливаем метод, что отражает прогресс. Подобная странноватая конструкция необходима для согласования потоков, т. к. прогресс бар доступен только из главного...
            await conversion.Start();//Поехали
        }

        private string? SelectFolder()
        {
            var dialog = new CommonOpenFileDialog();
            dialog.IsFolderPicker = true;//Мы выбираем только папки, это важно
            CommonFileDialogResult result = dialog.ShowDialog();
            if (result == CommonFileDialogResult.Ok) return dialog.FileName;
            return null;
        }
    }
}
