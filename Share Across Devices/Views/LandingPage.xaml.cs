﻿using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.System.RemoteSystems;
using Windows.UI.Core;
using Share_Across_Devices.Controls;
using System.Collections.ObjectModel;
using Windows.UI.Xaml.Hosting;
using System.Numerics;
using Windows.UI.Composition;
using Windows.Foundation.Metadata;
using Windows.UI;
using Windows.UI.ViewManagement;
using System.Threading.Tasks;
using Windows.UI.Xaml.Navigation;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Networking.Sockets;
using Windows.Storage;
using System.IO;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media;

namespace Share_Across_Devices.Views
{
    public sealed partial class LandingPage : Page
    {
        private RemoteSystemWatcher deviceWatcher;
        private Compositor _compositor;
        private RemoteDeviceObject selectedDevice;
        private bool sendOptionsHidden = true;
        private bool notificationsHidden = true;
        private bool openInBrowser = false;
        private bool openInTubeCast = false;
        private bool openInMyTube = false;
        private bool transferFile = false;
        private string fileName;
        private string textToCopy;
        private StorageFile file;

        ObservableCollection<RemoteDeviceObject> DeviceList = new ObservableCollection<RemoteDeviceObject>();

        public LandingPage()
        {
            this.InitializeComponent();
            this.setUpDevicesList();
            this.setUpCompositor();
            this.setTitleBar();
            this.DeviceList.CollectionChanged += DeviceList_CollectionChanged;
        }
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            var protocolArgs = e.Parameter as ProtocolActivatedEventArgs;
            
            if (protocolArgs != null)
            {
                this.SelectedDeviceIcon.Glyph = "\uE119";
                this.SelectedDeviceName.Text = "Receiving!";
                this.resetView();
                this.animateDeviceChosen();

                var queryStrings = new WwwFormUrlDecoder(protocolArgs.Uri.Query);
                if (!protocolArgs.Uri.Query.StartsWith("?FileName="))
                {
                    this.textToCopy = queryStrings.GetFirstValueByName("Text");
                    try
                    {
                        if (textToCopy.Length > 0)
                        {
                            DataPackage package = new DataPackage()
                            {
                                RequestedOperation = DataPackageOperation.Copy
                            };
                            package.SetText(textToCopy);
                            Clipboard.SetContent(package);
                            Clipboard.Flush();
                            this.NotificationText.Text = "Copied!";
                            this.animateShowNotification();
                        }
                    }
                    catch
                    {
                        this.NotificationText.Text = "Manual copy required, tap here to copy";
                        this.animateShowNotification();
                        this.NotificationText.Tapped += NotificationText_Tapped;
                    }

                }
                else
                {
                    this.fileName = queryStrings.GetFirstValueByName("FileName");
                    this.beginListeningForFile();
                }
            }
        }

        #region File retrieval 
        private async void beginListeningForFile()
        {
            this.NotificationText.Text = "Receiving file...";
            this.animateShowNotification();
            try
            {
                //Create a StreamSocketListener to start listening for TCP connections.
                StreamSocketListener socketListener = new StreamSocketListener();

                //Hook up an event handler to call when connections are received.
                socketListener.ConnectionReceived += SocketListener_ConnectionReceived;

                //Start listening for incoming TCP connections on the specified port. You can specify any port that' s not currently in use.
                await socketListener.BindServiceNameAsync("1717");
            }
            catch (Exception e)
            {
                this.NotificationText.Text = "Failed to receive file";
                this.animateShowNotification();
            }
        }
        private async void SocketListener_ConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            sender.ConnectionReceived -= SocketListener_ConnectionReceived;
            if (fileName != null)
            {
                try
                {
                    file = await ApplicationData.Current.LocalFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
                    //Read line from the remote client.
                    using (var fileStream = await file.OpenStreamForWriteAsync())
                    {
                        using (var inStream = args.Socket.InputStream.AsStreamForRead())
                        {
                            byte[] bytes;
                            DataReader dataReader = new DataReader(inStream.AsInputStream());
                            fileStream.Seek(0, SeekOrigin.Begin);
                            while (inStream.CanRead)
                            {
                                await dataReader.LoadAsync(sizeof(bool));
                                if (dataReader.ReadBoolean() == false)
                                {
                                    break;
                                }
                                await dataReader.LoadAsync(sizeof(Int32));
                                var byteSize = dataReader.ReadInt32();
                                bytes = new byte[byteSize];
                                await dataReader.LoadAsync(sizeof(Int32));
                                var percentComplete = dataReader.ReadInt32();
                                await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                                {
                                    this.NotificationText.Text = percentComplete + "% transferred";
                                    this.animateShowNotification();
                                });
                                await dataReader.LoadAsync((uint)byteSize);
                                dataReader.ReadBytes(bytes);
                                await fileStream.WriteAsync(bytes, 0, byteSize);
                            }
                        }
                    }

                    //Send the line back to the remote client.
                    Stream outStream = args.Socket.OutputStream.AsStreamForWrite();
                    StreamWriter writer = new StreamWriter(outStream);
                    await writer.WriteLineAsync("File Received!");
                    await writer.FlushAsync();

                    await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        this.SelectedDeviceIcon.Glyph = "\uE166";
                        this.SelectedDeviceName.Text = "Received!";
                        this.NotificationText.Text = "File received";
                        this.animateShowNotificationTimed();
                        TransferView transferView = new TransferView(this.file);
                        this.MediaRetrieveViewGrid.Children.Clear();
                        this.MediaRetrieveViewGrid.Children.Add(transferView);
                        this.showMediaRetrieveViewGrid();
                        transferView.CancelEvent += TransferView_CancelEvent;
                        transferView.SaveEvent += TransferView_SaveEvent;
                    });
                }
                catch
                {
                    await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        this.NotificationText.Text = "Transfer interrupted";
                        this.animateShowNotification();
                    });
                }
            }
        }
        #endregion

        #region Beauty and animations
        private void setTitleBar()
        {
            var appBlue = Color.FromArgb(255, 56, 118, 191);
            if (ApiInformation.IsTypePresent("Windows.UI.ViewManagement.ApplicationView"))
            {
                ApplicationView AppView = ApplicationView.GetForCurrentView();
                AppView.TitleBar.BackgroundColor = appBlue;
                AppView.TitleBar.ButtonInactiveBackgroundColor = appBlue;
                AppView.TitleBar.ButtonInactiveForegroundColor = Colors.White;
                AppView.TitleBar.ButtonBackgroundColor = appBlue;
                AppView.TitleBar.ButtonForegroundColor = Colors.White;
                AppView.TitleBar.ButtonHoverBackgroundColor = appBlue;
                AppView.TitleBar.ButtonHoverForegroundColor = Colors.White;
                AppView.TitleBar.ButtonPressedBackgroundColor = appBlue;
                AppView.TitleBar.ButtonPressedForegroundColor = Colors.White;
                AppView.TitleBar.ForegroundColor = Colors.White;
                AppView.TitleBar.InactiveBackgroundColor = appBlue;
                AppView.TitleBar.InactiveForegroundColor = Colors.White;
            }
            if (ApiInformation.IsTypePresent("Windows.UI.ViewManagement.StatusBar"))
            {
                var statusBar = StatusBar.GetForCurrentView();
                statusBar.BackgroundOpacity = 1;
                statusBar.BackgroundColor = appBlue;
                statusBar.ForegroundColor = Colors.White;
            }
        }
        private void setUpCompositor()
        {
            _compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
            var sendOptionsVisual = ElementCompositionPreview.GetElementVisual(this.SendOptionsPanel);
            sendOptionsVisual.Opacity = 0f;
            var devicePanelVisual = ElementCompositionPreview.GetElementVisual(this.DevicePanel);
            devicePanelVisual.Opacity = 0f;
            var notificationVisual = ElementCompositionPreview.GetElementVisual(this.NotificationPanel);
            notificationVisual.Opacity = 0f;
        }
        private void animateDeviceChosen()
        {
            var devicePanelVisual = ElementCompositionPreview.GetElementVisual(this.DevicePanel);

            Vector3KeyFrameAnimation offsetAnimation = _compositor.CreateVector3KeyFrameAnimation();
            offsetAnimation.Duration = TimeSpan.FromMilliseconds(1000);
            offsetAnimation.InsertKeyFrame(0f, new Vector3(0f, -100f, 0f));
            offsetAnimation.InsertKeyFrame(1f, new Vector3(0f, 0f, 0f));

            ScalarKeyFrameAnimation fadeAnimation = _compositor.CreateScalarKeyFrameAnimation();
            fadeAnimation.Duration = TimeSpan.FromMilliseconds(1000);
            fadeAnimation.InsertKeyFrame(0f, 0f);
            fadeAnimation.InsertKeyFrame(1f, 1f);

            devicePanelVisual.StartAnimation("Offset", offsetAnimation);
            devicePanelVisual.StartAnimation("Opacity", fadeAnimation);
        }
        private void animateHideNotification()
        {
            var itemVisual = ElementCompositionPreview.GetElementVisual(this.NotificationPanel);

            if (!this.notificationsHidden)
            {
                Vector3KeyFrameAnimation offsetAnimation = _compositor.CreateVector3KeyFrameAnimation();
                offsetAnimation.Duration = TimeSpan.FromMilliseconds(1000);
                offsetAnimation.InsertKeyFrame(0f, new Vector3(0f, 0f, 0f));
                offsetAnimation.InsertKeyFrame(1f, new Vector3(0f, -100f, 0f));

                ScalarKeyFrameAnimation fadeAnimation = _compositor.CreateScalarKeyFrameAnimation();
                fadeAnimation.Duration = TimeSpan.FromMilliseconds(1000);
                fadeAnimation.InsertKeyFrame(0f, 1f);
                fadeAnimation.InsertKeyFrame(1f, 0f);

                itemVisual.StartAnimation("Offset", offsetAnimation);
                itemVisual.StartAnimation("Opacity", fadeAnimation);
                this.notificationsHidden = true;
            }
        }
        private void animateShowNotification()
        {
            var itemVisual = ElementCompositionPreview.GetElementVisual(this.NotificationPanel);

            if (this.notificationsHidden)
            {
                Vector3KeyFrameAnimation offsetAnimation = _compositor.CreateVector3KeyFrameAnimation();
                offsetAnimation.Duration = TimeSpan.FromMilliseconds(1000);
                offsetAnimation.InsertKeyFrame(0f, new Vector3(0f, -100f, 0f));
                offsetAnimation.InsertKeyFrame(1f, new Vector3(0f, 0f, 0f));

                ScalarKeyFrameAnimation fadeAnimation = _compositor.CreateScalarKeyFrameAnimation();
                fadeAnimation.Duration = TimeSpan.FromMilliseconds(1000);
                fadeAnimation.InsertKeyFrame(0f, 0f);
                fadeAnimation.InsertKeyFrame(1f, 1f);

                itemVisual.StartAnimation("Offset", offsetAnimation);
                itemVisual.StartAnimation("Opacity", fadeAnimation);
                this.notificationsHidden = false;
            }
        }
        private void animateButtonEnabled(Button button)
        {
            var itemVisual = ElementCompositionPreview.GetElementVisual(button);
            float width = (float)button.RenderSize.Width;
            float height = (float)button.RenderSize.Height;
            itemVisual.CenterPoint = new Vector3(width / 2, height / 2, 0f);

            Vector3KeyFrameAnimation scaleAnimation = _compositor.CreateVector3KeyFrameAnimation();
            scaleAnimation.Duration = TimeSpan.FromMilliseconds(1000);
            scaleAnimation.InsertKeyFrame(0f, new Vector3(1f, 1f, 1f));

            if (button.IsEnabled)
            {
                scaleAnimation.InsertKeyFrame(0.1f, new Vector3(1.1f, 1.1f, 1.1f));
            }
            else
            {
                scaleAnimation.InsertKeyFrame(0.1f, new Vector3(0.9f, 0.9f, 0.9f));
            }

            scaleAnimation.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f));
            itemVisual.StartAnimation("Scale", scaleAnimation);
        }
        private void hideSendOptionsPanel()
        {
            var itemVisual = ElementCompositionPreview.GetElementVisual(this.SendOptionsPanel);

            if (!this.sendOptionsHidden)
            {
                Vector3KeyFrameAnimation offsetAnimation = _compositor.CreateVector3KeyFrameAnimation();
                offsetAnimation.Duration = TimeSpan.FromMilliseconds(1000);
                offsetAnimation.InsertKeyFrame(0f, new Vector3(0f, 0f, 0f));
                offsetAnimation.InsertKeyFrame(1f, new Vector3(0f, 100f, 0f));

                ScalarKeyFrameAnimation fadeAnimation = _compositor.CreateScalarKeyFrameAnimation();
                fadeAnimation.Duration = TimeSpan.FromMilliseconds(1000);
                fadeAnimation.InsertKeyFrame(0f, 1f);
                fadeAnimation.InsertKeyFrame(1f, 0f);

                itemVisual.StartAnimation("Offset", offsetAnimation);
                itemVisual.StartAnimation("Opacity", fadeAnimation);
                this.sendOptionsHidden = true;
            }
            this.openInBrowser = false;
            this.openInMyTube = false;
            this.openInTubeCast = false;
        }

        private void showSendOptionsPanel()
        {
            var itemVisual = ElementCompositionPreview.GetElementVisual(this.SendOptionsPanel);

            if (this.sendOptionsHidden)
            {
                Vector3KeyFrameAnimation offsetAnimation = _compositor.CreateVector3KeyFrameAnimation();
                offsetAnimation.Duration = TimeSpan.FromMilliseconds(1000);
                offsetAnimation.InsertKeyFrame(0f, new Vector3(0f, 100f, 0f));
                offsetAnimation.InsertKeyFrame(1f, new Vector3(0f, 0f, 0f));

                ScalarKeyFrameAnimation fadeAnimation = _compositor.CreateScalarKeyFrameAnimation();
                fadeAnimation.Duration = TimeSpan.FromMilliseconds(1000);
                fadeAnimation.InsertKeyFrame(0f, 0f);
                fadeAnimation.InsertKeyFrame(1f, 1f);

                itemVisual.StartAnimation("Offset", offsetAnimation);
                itemVisual.StartAnimation("Opacity", fadeAnimation);
                this.sendOptionsHidden = false;
            }
        }
        private void showMediaViewGrid()
        {
            if (this.MediaSendViewGrid.Children[0] != null)
            {
                var itemVisual = ElementCompositionPreview.GetElementVisual(this.MediaSendViewGrid.Children[0]);

                Vector3KeyFrameAnimation offsetAnimation = _compositor.CreateVector3KeyFrameAnimation();
                offsetAnimation.Duration = TimeSpan.FromMilliseconds(1000);
                offsetAnimation.InsertKeyFrame(0f, new Vector3(0f, 300f, 0f));
                offsetAnimation.InsertKeyFrame(1f, new Vector3(0f, 0f, 0f));

                ScalarKeyFrameAnimation fadeAnimation = _compositor.CreateScalarKeyFrameAnimation();
                fadeAnimation.Duration = TimeSpan.FromMilliseconds(1000);
                fadeAnimation.InsertKeyFrame(0f, 0f);
                fadeAnimation.InsertKeyFrame(1f, 1f);

                itemVisual.StartAnimation("Offset", offsetAnimation);
                itemVisual.StartAnimation("Opacity", fadeAnimation);
            }
        }
        private void showMediaRetrieveViewGrid()
        {
            if (this.MediaRetrieveViewGrid.Children[0] != null)
            {
                var itemVisual = ElementCompositionPreview.GetElementVisual(this.MediaRetrieveViewGrid.Children[0]);

                Vector3KeyFrameAnimation offsetAnimation = _compositor.CreateVector3KeyFrameAnimation();
                offsetAnimation.Duration = TimeSpan.FromMilliseconds(1000);
                offsetAnimation.InsertKeyFrame(0f, new Vector3(0f, -300f, 0f));
                offsetAnimation.InsertKeyFrame(1f, new Vector3(0f, 0f, 0f));

                ScalarKeyFrameAnimation fadeAnimation = _compositor.CreateScalarKeyFrameAnimation();
                fadeAnimation.Duration = TimeSpan.FromMilliseconds(1000);
                fadeAnimation.InsertKeyFrame(0f, 0f);
                fadeAnimation.InsertKeyFrame(1f, 1f);

                itemVisual.StartAnimation("Offset", offsetAnimation);
                itemVisual.StartAnimation("Opacity", fadeAnimation);
            }
        }
        private void hideMediaViewGrid()
        {
            if (this.MediaSendViewGrid.Children[0] != null)
            {
                var itemVisual = ElementCompositionPreview.GetElementVisual(this.MediaSendViewGrid.Children[0]);

                Vector3KeyFrameAnimation offsetAnimation = _compositor.CreateVector3KeyFrameAnimation();
                offsetAnimation.Duration = TimeSpan.FromMilliseconds(1000);
                offsetAnimation.InsertKeyFrame(0f, new Vector3(0f, 0f, 0f));
                offsetAnimation.InsertKeyFrame(1f, new Vector3(0f, 300f, 0f));

                ScalarKeyFrameAnimation fadeAnimation = _compositor.CreateScalarKeyFrameAnimation();
                fadeAnimation.Duration = TimeSpan.FromMilliseconds(1000);
                fadeAnimation.InsertKeyFrame(0f, 1f);
                fadeAnimation.InsertKeyFrame(1f, 0f);

                itemVisual.StartAnimation("Offset", offsetAnimation);
                itemVisual.StartAnimation("Opacity", fadeAnimation);
            }
        }
        private void hideMediaRetrieveViewGrid()
        {
            if (this.MediaRetrieveViewGrid.Children[0] != null)
            {
                var itemVisual = ElementCompositionPreview.GetElementVisual(this.MediaRetrieveViewGrid.Children[0]);

                Vector3KeyFrameAnimation offsetAnimation = _compositor.CreateVector3KeyFrameAnimation();
                offsetAnimation.Duration = TimeSpan.FromMilliseconds(1000);
                offsetAnimation.InsertKeyFrame(0f, new Vector3(0f, 0f, 0f));
                offsetAnimation.InsertKeyFrame(1f, new Vector3(0f, -300f, 0f));

                ScalarKeyFrameAnimation fadeAnimation = _compositor.CreateScalarKeyFrameAnimation();
                fadeAnimation.Duration = TimeSpan.FromMilliseconds(1000);
                fadeAnimation.InsertKeyFrame(0f, 1f);
                fadeAnimation.InsertKeyFrame(1f, 0f);

                itemVisual.StartAnimation("Offset", offsetAnimation);
                itemVisual.StartAnimation("Opacity", fadeAnimation);
            }
        }
        #endregion

        #region Remote system methods
        private void DeviceList_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            this.HamburgerMenu.ItemsSource = DeviceList;
        }
        private async void setUpDevicesList()
        {
            RemoteSystemAccessStatus accessStatus = await RemoteSystem.RequestAccessAsync();

            if (accessStatus == RemoteSystemAccessStatus.Allowed)
            {
                deviceWatcher = RemoteSystem.CreateWatcher();
                deviceWatcher.RemoteSystemAdded += DeviceWatcher_RemoteSystemAdded;
                deviceWatcher.RemoteSystemUpdated += DeviceWatcher_RemoteSystemUpdated;
                deviceWatcher.RemoteSystemRemoved += DeviceWatcher_RemoteSystemRemoved;
                deviceWatcher.Start();
            }
        }
        private async void DeviceWatcher_RemoteSystemAdded(RemoteSystemWatcher sender, RemoteSystemAddedEventArgs args)
        {
            var remoteSystem = args.RemoteSystem;
            await this.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                RemoteDeviceObject device = new RemoteDeviceObject(remoteSystem);
                this.DeviceList.Add(device);
            });
        }
        private async void DeviceWatcher_RemoteSystemUpdated(RemoteSystemWatcher sender, RemoteSystemUpdatedEventArgs args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                foreach (RemoteDeviceObject device in this.DeviceList)
                {
                    if (device.RemoteSystem.Id == args.RemoteSystem.Id)
                    {
                        device.RemoteSystem = args.RemoteSystem;
                        if (this.selectedDevice != null && this.selectedDevice.RemoteSystem.Id == args.RemoteSystem.Id)
                        {
                            this.selectedDevice.RemoteSystem = args.RemoteSystem;
                        }
                        this.validateTextAndButtons();
                        return;
                    }
                }
            });
        }
        private async void DeviceWatcher_RemoteSystemRemoved(RemoteSystemWatcher sender, RemoteSystemRemovedEventArgs args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                foreach (RemoteDeviceObject device in this.DeviceList)
                {
                    if (device.RemoteSystem.Id == args.RemoteSystemId)
                    {
                        this.DeviceList.Remove(device);
                        if (this.selectedDevice != null && this.selectedDevice.RemoteSystem.Id == args.RemoteSystemId)
                        {
                            this.selectedDevice = null;
                        }
                        this.validateTextAndButtons();
                        return;
                    }
                }
            });
        }
        #endregion     

        #region UI events
        private async void TransferView_SaveEvent(object sender, EventArgs e)
        {
            this.hideMediaRetrieveViewGrid();
            this.NotificationText.Text = "File saved!";
            this.animateShowNotificationTimed();
            await Task.Delay(1000);
            this.MediaRetrieveViewGrid.Children.Clear();
        }
        private void NotificationText_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            DataPackage package = new DataPackage()
            {
                RequestedOperation = DataPackageOperation.Copy
            };
            package.SetText(this.textToCopy);
            Clipboard.SetContent(package);
            Clipboard.Flush();
            this.NotificationText.Tapped -= NotificationText_Tapped;
            this.NotificationText.Text = "Copied";
            this.animateShowNotificationTimed();
        }
        private async void TransferView_CancelEvent(object sender, EventArgs e)
        {
            this.hideMediaRetrieveViewGrid();
            await Task.Delay(1000);
            this.MediaRetrieveViewGrid.Children.Clear();
        }
        private void MessageToSend_TextChanged(object sender, TextChangedEventArgs e)
        {
            this.validateTextAndButtons();
        }
        private void Button_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            var button = sender as Button;
            this.animateButtonEnabled(button);
        }
        private void HamburgerMenu_ItemClick(object sender, ItemClickEventArgs e)
        {
            var clickedItem = e.ClickedItem as RemoteDeviceObject;
            this.selectedDevice = clickedItem;
            this.selectedDevice.NotifyEvent += SelectedDevice_NotifyEvent;
            this.SelectedDeviceIcon.Glyph = clickedItem.DeviceIcon;
            this.SelectedDeviceName.Text = clickedItem.DeviceName;
            if (this.VisualStateGroup.CurrentState == this.VisualStatePhone)
            {
                this.HamburgerMenu.IsPaneOpen = false;
            }
            this.resetView();
            this.animateDeviceChosen();
            this.validateTextAndButtons();
        }
        private void SelectedDevice_NotifyEvent(object sender, MyEventArgs e)
        {
            var message = e.Message;
            this.NotificationText.Text = message;
            if (e.MessageType == MyEventArgs.messageType.Indefinite)
            {
                this.animateShowNotification();
            }
            else
            {
                this.animateShowNotificationTimed();
            }
        }
        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.selectedDevice != null)
            {
                if (this.openInBrowser)
                {
                    this.selectedDevice.OpenLinkInBrowser(this.MessageToSend.Text);
                }
                else if (this.openInMyTube)
                {
                    this.selectedDevice.OpenLinkInMyTube(this.MessageToSend.Text);
                }
                else if (this.openInTubeCast)
                {
                    this.selectedDevice.OpenLinkInTubeCast(this.MessageToSend.Text);
                }
                else if (this.transferFile)
                {
                    this.selectedDevice.SendFile();
                }
                else
                {
                    this.selectedDevice.ShareMessage(this.MessageToSend.Text);
                }
            }
        }
        private async void AttachButton_Click(object sender, RoutedEventArgs e)
        {
            var file = await this.selectedDevice.OpenFileToSend();
            if (file != null)
            {
                this.transferFile = true;
                this.openInBrowser = false;
                this.openInMyTube = false;
                this.openInTubeCast = false;
                this.SendButton.IsEnabled = true;
                this.MessageToSend.IsEnabled = false;
                this.hideSendOptionsPanel();
                var mediaViewer = new MediaView(file);
                this.MediaSendViewGrid.Children.Clear();
                this.MediaSendViewGrid.Children.Add(mediaViewer);
                this.showMediaViewGrid();
            }
        }
        private void OpenInGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (this.OpenInGridView.SelectedIndex >= 0)
            {
                if (e.AddedItems[0] == this.OpenInBrowserButton)
                {
                    this.openInBrowser = true;
                    this.openInMyTube = false;
                    this.openInTubeCast = false;
                }
                else if (e.AddedItems[0] == this.OpenInMyTubeButton)
                {
                    this.openInMyTube = true;
                    this.openInBrowser = false;
                    this.openInTubeCast = false;
                }
                else if (e.AddedItems[0] == this.OpenInTubeCastButton)
                {
                    this.openInTubeCast = true;
                    this.openInBrowser = false;
                    this.openInMyTube = false;
                }
            }
        }
        private void OpenInButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender == this.OpenInBrowserButton)
            {
                if (this.openInBrowser != true)
                {
                    this.openInBrowser = true;
                    this.OpenInBrowserButton.BorderBrush = new SolidColorBrush(Colors.White);
                }
                else
                {
                    this.openInBrowser = false;
                    this.OpenInBrowserButton.BorderBrush = new SolidColorBrush(Colors.Transparent);
                }
                this.openInMyTube = false;
                this.openInTubeCast = false;
                this.OpenInMyTubeButton.BorderBrush = new SolidColorBrush(Colors.Transparent);
                this.OpenInTubeCastButton.BorderBrush = new SolidColorBrush(Colors.Transparent);
            }
            else if (sender == this.OpenInMyTubeButton)
            {
                if (this.openInMyTube != true)
                {
                    this.openInMyTube = true;
                    this.OpenInMyTubeButton.BorderBrush = new SolidColorBrush(Colors.White);
                }
                else
                {
                    this.openInMyTube = false;
                    this.OpenInMyTubeButton.BorderBrush = new SolidColorBrush(Colors.Transparent);
                }
                this.openInBrowser = false;
                this.openInTubeCast = false;
                this.OpenInBrowserButton.BorderBrush = new SolidColorBrush(Colors.Transparent);
                this.OpenInTubeCastButton.BorderBrush = new SolidColorBrush(Colors.Transparent);
            }
            else if (sender == this.OpenInTubeCastButton)
            {
                if (this.openInTubeCast != true)
                {
                    this.openInTubeCast = true;
                    this.OpenInTubeCastButton.BorderBrush = new SolidColorBrush(Colors.White);
                }
                else
                {
                    this.openInTubeCast = false;
                    this.OpenInTubeCastButton.BorderBrush = new SolidColorBrush(Colors.Transparent);
                }
                this.openInBrowser = false;
                this.openInMyTube = false;
                this.OpenInBrowserButton.BorderBrush = new SolidColorBrush(Colors.Transparent);
                this.OpenInMyTubeButton.BorderBrush = new SolidColorBrush(Colors.Transparent);
            }
        }
        #endregion

        #region Helpers
        private async void animateShowNotificationTimed()
        {
            this.animateShowNotification();
            await Task.Delay(4000);
            this.animateHideNotification();
        }
        private void validateTextAndButtons()
        {
            if (this.selectedDevice != null)
            {
                if (this.remoteSystemIsLocal())
                {
                    this.AttachButton.IsEnabled = true;
                }
                else
                {
                    this.AttachButton.IsEnabled = false;
                }

                if (this.MessageToSend.Text.Length > 0)
                {
                    this.SendButton.IsEnabled = true;
                    this.checkIfWebLink();
                }
                else
                {
                    this.SendButton.IsEnabled = false;
                    this.hideSendOptionsPanel();
                }
            }
            else
            {
                this.AttachButton.IsEnabled = false;
                this.SendButton.IsEnabled = false;
                this.hideSendOptionsPanel();
            }
        }
        private void checkIfWebLink()
        {
            if (this.MessageToSend.Text.ToLower().StartsWith("http://") || this.MessageToSend.Text.ToLower().StartsWith("https://"))
            {
                this.showSendOptionsPanel();
                this.OpenInBrowserButton.IsEnabled = true;
                if (this.MessageToSend.Text.ToLower().Contains("youtube.com/watch?"))
                {
                    this.OpenInMyTubeButton.IsEnabled = true;
                    this.OpenInTubeCastButton.IsEnabled = true;
                }
                else
                {
                    this.OpenInMyTubeButton.IsEnabled = false;
                    this.OpenInTubeCastButton.IsEnabled = false;
                }
            }
            else
            {
                this.OpenInBrowserButton.IsEnabled = false;
                this.OpenInMyTubeButton.IsEnabled = false;
                this.OpenInTubeCastButton.IsEnabled = false;
                this.hideSendOptionsPanel();
            }
        }    
        private bool remoteSystemIsLocal()
        {
            return this.selectedDevice.RemoteSystem.IsAvailableByProximity;
        }
        private void resetView()
        {
            this.MessageToSend.IsEnabled = true;
            this.MessageToSend.Text = "";
            this.openInBrowser = false;
            this.openInMyTube = false;
            this.openInTubeCast = false;
        }
        #endregion
        
    }
}