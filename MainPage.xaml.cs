using Microsoft.Maui.Media;
using SkiaSharp;
using PdfSharpCore.Pdf;
using PdfSharpCore.Drawing;
using System.IO;
using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel;
using System.ComponentModel;



//это используется для отлова исключений, в релизе можете проверки подчистить
//чтобы в блоксхему не вписывать лишних блоков, если сможете объяснить что это
//и для чего работает, будет плюсом при защите 
//InvalidOperationException("{context}");

namespace androidpdf
{
    public class MainPageViewModel : INotifyPropertyChanged
    {
        private bool _isRunning;

        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (_isRunning != value)
                {
                    _isRunning = value;
                    OnPropertyChanged(nameof(IsRunning));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
            BindingContext = new MainPageViewModel();
        }

        private async void OnSaveToPdfClicked(object sender, EventArgs e)
        {
            ((MainPageViewModel)BindingContext).IsRunning = true;
            


            try
            {
                // Пытаемся заново получить поток для изображения
                var photo = await MediaPicker.CapturePhotoAsync();
                if (photo == null)
                {
                    
                    await DisplayAlert("Ошибка", "Фото не было захвачено.", "ОК");
                    return;
                }
                
                // Открываем поток изображения для сохранения в PDF
                using var stream = await photo.OpenReadAsync();
                var capturedImageStream = new MemoryStream();
                await stream.CopyToAsync(capturedImageStream);
                capturedImageStream.Position = 0;

                ScannedImage.Source = ImageSource.FromStream(() => new MemoryStream(capturedImageStream.ToArray()));
                EnsureStreamIsAvailable(capturedImageStream, "Перед сохранением в PDF");

                
                
                var filePath = await Task.Run(() => SaveImageToPdf(capturedImageStream));
                Console.WriteLine($"PDF сохранён в: {filePath}");
                await DisplayAlert("Успех", $"PDF сохранён: {filePath}", "ОК");

                //предложение открыть файл
                await Share.RequestAsync(new ShareFileRequest
                {
                    Title = "Открыть PDF",
                    File = new ShareFile(filePath)
                });

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при сохранении PDF: {ex}");
                await DisplayAlert("Ошибка", $"Не удалось сохранить PDF: {ex.Message}", "ОК");
                ((MainPageViewModel)BindingContext).IsRunning = false;
            }
            finally
            {
                ((MainPageViewModel)BindingContext).IsRunning = false;
                

            }
        }

        private string SaveImageToPdf(MemoryStream capturedImageStream)
        {
            EnsureStreamIsAvailable(capturedImageStream, "В начале SaveImageToPdf");

            // Получаем безопасный путь для сохранения в AppDataDirectory
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Downloads");

            // Убедимся, что каталог существует
            if (!Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
            }


            // Генерируем уникальное имя файла с использованием временной метки
            var uniqueFileName = $"ScannedDocument_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";

            // Генерируем путь для сохранения файла
            var filePath = Path.Combine(appDataPath, uniqueFileName);

            using var pdfDocument = new PdfDocument();
            var pdfPage = pdfDocument.AddPage();

            capturedImageStream.Position = 0;
            EnsureStreamIsAvailable(capturedImageStream, "Перед декодированием изображения");

            // Декодируем изображение
            using var skBitmap = SKBitmap.Decode(capturedImageStream);
            if (skBitmap == null)
            {
                throw new InvalidOperationException("Ошибка при декодировании изображения.");
            }

            // Рассчитываем размеры для нового изображения
            int newHeight = skBitmap.Height * 1000 / skBitmap.Width;

            // Указываем параметры с использованием SKSamplingOptions
            //
            //     конструктор:
            //SKImageInfo(int width, int height, SKColorType colorType = SKColorType.Bgra8888, SKAlphaType alphaType = SKAlphaType.Premul, int rowBytes = 0);

            var resizeInfo = new SKImageInfo(1000, newHeight);
            //чтобы поменять качество изображения меняй Linear 
            //доступно : 
            /*
            
             * 1. Упрощённый, пикселизированный вид
            
            var options = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
            Этот вариант подойдёт для ретро-стиля, где вам нужны чёткие "пиксельные" изображения.

            2. Высокое качество, без мипмапов
           
            var options = new SKSamplingOptions(SKFilterMode.Cubic, SKMipmapMode.None);
            Подходит для высококачественной обработки, например, для печати.

            3. Использование мипмапов с линейной фильтрацией
           
            var options = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
            Идеально для масштабирования изображений в играх или приложениях с высокой производительностью.

            4. Экономия производительности при уменьшении
            
            var options = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.Nearest);
            Используется для быстрого уменьшения размера изображения.
         
             */
            var samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);

            // Масштабируем изображение
            using var resizedBitmap = skBitmap.Resize(resizeInfo, samplingOptions);
            if (resizedBitmap == null)
            {
                throw new InvalidOperationException("Ошибка при изменении размера изображения.");
            }

            // Создаем изображение из масштабированного битмапа
            using var skImage = SKImage.FromBitmap(resizedBitmap);

            // Создаем поток для изображения
            using var imageStream = new MemoryStream();
            skImage.Encode(SKEncodedImageFormat.Jpeg, 80).SaveTo(imageStream);
            imageStream.Position = 0;

            EnsureStreamIsAvailable(imageStream, "Перед рисованием изображения в PDF");

            // Получаем объект XGraphics для рисования в PDF
            using var xGraphics = XGraphics.FromPdfPage(pdfPage);

            // Рисуем изображение на странице PDF
            using var image = XImage.FromStream(() => imageStream);
            xGraphics.DrawImage(image, 0, 0, pdfPage.Width, pdfPage.Height);

            // Записываем PDF-файл
            using var fileStream = File.Create(filePath);
            pdfDocument.Save(fileStream);

            // Возвращаем путь к сохраненному PDF

            return filePath;
        }


//проверка доступности потоков
//тоже можно потом удалить
//но надо почистить много чего
//так что лучше не трогайть
        private void EnsureStreamIsAvailable(Stream? stream, string context)
        {
            if (stream == null)
                throw new InvalidOperationException($"Поток недоступен. Контекст: {context}");

            if (!stream.CanRead || !stream.CanSeek)
                throw new InvalidOperationException($"Поток не поддерживает чтение или перемотку. Контекст: {context}");

            Console.WriteLine($"Поток доступен. Контекст: {context}, Длина: {stream.Length}, Позиция: {stream.Position}");
        }
    }
}
