using System; 
using System.Diagnostics; 
using System.Linq;
using System.Threading.Tasks; 
using Windows.ApplicationModel.Contacts; 
using Windows.Devices.Enumeration; 
using Windows.Foundation; 
using Windows.Globalization; 
using Windows.Graphics.Imaging; 
using Windows.Media.Capture; 
using Windows.Media.MediaProperties; 
using Windows.Media.Ocr; 
using Windows.Storage; 
using Windows.Storage.Streams; 
using Windows.UI.Xaml; 
using Windows.UI.Xaml.Controls; 
using Windows.UI.Xaml.Media; 
using Windows.UI.Xaml.Media.Imaging;


// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace BusinessCardReader
{
    public sealed partial class MainPage : Page
    {
        #region Global Methods //turned into vars to be used throughout app, creates constant.
        private DeviceInformationCollection _allVideoDevices;
        private DeviceInformation _desiredDevice;
        private MediaCapture _mediaCapture;
        private StorageFile _photoFile;
        private OcrEngine _orcEngine;
        private string _phoneNumber = string.Empty;
        #endregion
        public MainPage()
        {
            InitializeComponent();
            Loaded += MainPage_Loaded;
            _orcEngine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en"));
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            //searches for video devices
            _allVideoDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            if (_allVideoDevices == null || !_allVideoDevices.Any())
                //Provides error in case of no devices
            {
                Debug.WriteLine("No devices found.");
                return;
            }
            foreach (DeviceInformation camera in _allVideoDevices)
            {
                if (CameraSelectionList.Items != null)
                {
                    CameraSelectionList.Items.Add(camera.Name);
                }
            }
        }
        //provides options in camera selection list
        private async void CameraSelectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string selectedCameraItem = e.AddedItems.FirstOrDefault().ToString();
            foreach (DeviceInformation item in _allVideoDevices)
            {
                if (string.Equals(item.Name, selectedCameraItem))
                {
                    _desiredDevice = item;
                    await StartDeviceAsync();
                }
            }

        }
        //provides button click function
        private async void TakePhotoButton_ClickAsync(object sender, RoutedEventArgs e)
        {
            try
            {
                Debug.WriteLine("Taking photo");
                TakePhotoButton.IsEnabled = false;
                _photoFile = await KnownFolders.PicturesLibrary.CreateFileAsync("capturedImage", CreationCollisionOption.ReplaceExisting);
                Debug.WriteLine("Create photo file successful");
                ImageEncodingProperties imageProperties = ImageEncodingProperties.CreateJpeg();
                await _mediaCapture.CapturePhotoToStorageFileAsync(imageProperties, _photoFile);
                TakePhotoButton.IsEnabled = true;
                Debug.WriteLine("Photo taken");
                ImageElement.Source = await OpenImageAsBitmapAsync(_photoFile);
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
                TakePhotoButton.IsEnabled = true;
            }
        }
        //provides button click function
        private async void GetDetailsButton_ClickAsync(object sender, RoutedEventArgs e)
        {
            GetDetailsErrorTextBlock.Text = string.Empty;

            if (_photoFile != null)
            {
                using (IRandomAccessStream stream = await _photoFile.OpenAsync(FileAccessMode.Read))
                {
                    BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
                    SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync();
                    OcrResult result = await _orcEngine.RecognizeAsync(bitmap);
                    if (string.IsNullOrEmpty(result.Text))
                    {
                        GetDetailsErrorTextBlock.Text = "Text not recognizable, try again.";
                        Debug.WriteLine("The Text is not recognizable.");
                    }
                    else
                    {
                        Debug.WriteLine(result.Text);
                        ApplyPatternMatching(result);
                    }
                }
            }
        }

        #region //Methods affecting camera
        //turns on video device prepares for capture
        private async Task StartDeviceAsync()
        {
            if (_desiredDevice != null)
            {
                try
                {
                    Debug.WriteLine("Starting device");
                    _mediaCapture = new MediaCapture();

                    await _mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings {VideoDeviceId = _desiredDevice.Id });
                    if (_mediaCapture.MediaCaptureSettings.VideoDeviceId != string.Empty && _mediaCapture.MediaCaptureSettings.AudioDeviceId != string.Empty)
                    {
                        TakePhotoButton.IsEnabled = true;
                        Debug.WriteLine("Device initialized successful");
                        await StartPreviewAsync();
                    }
                    else
                    {
                        TakePhotoButton.IsEnabled = false;
                        Debug.WriteLine("Error - No Video Device/Audio Device Found!");
                    }
                }
                catch (Exception exception)
                {
                    Debug.WriteLine(exception);
                }
            }
        }
        //allows preview window
        private async Task StartPreviewAsync()
        {
            try
            {
                Debug.WriteLine("Starting preview");
                PreviewElement.Source = _mediaCapture;
                await _mediaCapture.StartPreviewAsync();

                Debug.WriteLine("Start preview successful");
            }
            catch (Exception exception)
            {
                PreviewElement.Source = null;
                Debug.WriteLine(exception);
            }
        }
        #endregion

        #region //Helper Methods
        //provides bitmap image
        private async Task<BitmapImage> OpenImageAsBitmapAsync(StorageFile file)
        {
            IRandomAccessStreamWithContentType stream = await file.OpenReadAsync();
            BitmapImage bmpImg = new BitmapImage();
            bmpImg.SetSource(stream);
            return bmpImg;
        }

        private static Rect GetElementRect(FrameworkElement element)
        {
            GeneralTransform transform = element.TransformToVisual(null);
            Point point = transform.TransformPoint(new Point());
            return new Rect(point, new Size(element.ActualWidth, element.ActualHeight));
        }
        #endregion

        #region //OCR_Methods
        //Matches data from bitmap to contact info
        private void ApplyPatternMatching(OcrResult ocrResult)
        {
            Contact contact = new Contact();
            contact.SourceDisplayPicture = _photoFile;

            this.RepeatForOcrWords(ocrResult, (result, word) =>
            {
                switch (CardRecognizer.Recognize(word.Text))
                {
                    case RecognitionType.Other:
                        break;
                    case RecognitionType.Email:
                        contact.Emails.Add(new ContactEmail() { Address = word.Text });
                        break;
                    case RecognitionType.Name:
                        contact.FirstName = word.Text;
                        break;
                    case RecognitionType.Number:
                        _phoneNumber += word.Text;
                        RecognitionType type = CardRecognizer.Recognize(_phoneNumber);
                        if (type == RecognitionType.PhoneNumber)
                        {
                            contact.Phones.Add(new ContactPhone() { Number = _phoneNumber });
                        }
                        break;
                    case RecognitionType.WebPage:
                        try
                        {
                            contact.Websites.Add(new ContactWebsite() { Uri = new Uri(word.Text) });
                        }
                        catch (Exception)
                        {
                            Debug.WriteLine("OCR Result cannot be converted to a URI");
                        }
                        break;
                    default:
                        break;
                }
            });
            #region //This requires at least a phone or email to be valid
            if (!contact.Phones.Any())
            {
                if (!contact.Emails.Any())
                {
                    Debug.WriteLine("Contact must have phone or email info.");
                    return;
                }
            }
            #endregion
            Rect rect = GetElementRect(GetDetailsButton);
            ContactManager.ShowContactCard(contact, rect, Windows.UI.Popups.Placement.Default);
        }
        private void RepeatForOcrWords(OcrResult ocrResult, Action<OcrResult, OcrWord> repeater)
        {
            if (ocrResult.Lines != null)
            {
                foreach (var line in ocrResult.Lines)
                {
                    foreach (var word in line.Words)
                    {
                        repeater(ocrResult, word);
                    }
                }
            }
        }
        #endregion

    }
}
