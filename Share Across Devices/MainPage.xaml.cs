﻿using System;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation.Metadata;
using Windows.System;
using Windows.System.RemoteSystems;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.UI.Composition;
using Windows.UI.Xaml.Hosting;
using System.Numerics;
using Windows.UI.Xaml.Media;

namespace Share_Across_Devices
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private RemoteSystemWatcher deviceWatcher;
        private Compositor _compositor;

        public MainPage()
        {
            this.InitializeComponent();
            _compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
            this.setUpDevicesList();
            this.setTitleBar();
        }

        private void setTitleBar()
        {

            if (ApiInformation.IsTypePresent("Windows.UI.ViewManagement.ApplicationView"))
            {
                ApplicationView AppView = ApplicationView.GetForCurrentView();
                AppView.TitleBar.BackgroundColor = Colors.SlateGray;
                AppView.TitleBar.ButtonInactiveBackgroundColor = Colors.SlateGray;
                AppView.TitleBar.ButtonInactiveForegroundColor = Colors.White;
                AppView.TitleBar.ButtonBackgroundColor = Colors.SlateGray;
                AppView.TitleBar.ButtonForegroundColor = Colors.White;
                AppView.TitleBar.ButtonHoverBackgroundColor = Colors.SlateGray;
                AppView.TitleBar.ButtonHoverForegroundColor = Colors.White;
                AppView.TitleBar.ButtonPressedBackgroundColor = Colors.SlateGray;
                AppView.TitleBar.ButtonPressedForegroundColor = Colors.White;
                AppView.TitleBar.ForegroundColor = Colors.White;
                AppView.TitleBar.InactiveBackgroundColor = Colors.SlateGray;
                AppView.TitleBar.InactiveForegroundColor = Colors.White;
            }
            if (ApiInformation.IsTypePresent("Windows.UI.ViewManagement.StatusBar"))
            {
                var statusBar = StatusBar.GetForCurrentView();
                statusBar.BackgroundOpacity = 1;
                statusBar.BackgroundColor = Colors.SlateGray;
                statusBar.ForegroundColor = Colors.White;
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            var protocolArgs = e.Parameter as ProtocolActivatedEventArgs;


            // Set the ProtocolForResultsOperation field.
            if (protocolArgs != null)
            {
                var queryStrings = new WwwFormUrlDecoder(protocolArgs.Uri.Query);
                string textToCopy = "";
                try
                {
                    textToCopy = queryStrings.GetFirstValueByName("Text");
                    if (textToCopy.Length > 0)
                    {
                        DataPackage package = new DataPackage()
                        {
                            RequestedOperation = DataPackageOperation.Copy
                        };
                        package.SetText(textToCopy);
                        Clipboard.SetContent(package);
                        Clipboard.Flush();
                        NotifyUser("Copied!", NotifyType.StatusMessage);
                    }
                }
                catch
                {
                    NotifyUser("Manual copy required", NotifyType.StatusMessage);
                    this.ClipboardText.Text = textToCopy;
                    this.CopyToLocalClipboardButton.Visibility = Visibility.Visible;
                    this.CopyToLocalClipboardButton.IsEnabled = true;
                }
            }
        }

        private async void setUpDevicesList()
        {
            RemoteSystemAccessStatus accessStatus = await RemoteSystem.RequestAccessAsync();

            if (accessStatus == RemoteSystemAccessStatus.Allowed)
            {
                deviceWatcher = RemoteSystem.CreateWatcher();
                deviceWatcher.RemoteSystemAdded += DeviceWatcher_RemoteSystemAdded;
                deviceWatcher.Start();
            }
        }

        private async void DeviceWatcher_RemoteSystemAdded(RemoteSystemWatcher sender, RemoteSystemAddedEventArgs args)
        {
            var remoteSystem = args.RemoteSystem;
            await this.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (!this.DeviceListBox.Items.Contains(remoteSystem))
                {
                    this.DeviceListBox.Items.Add(remoteSystem);
                }
            });
        }
        private async void openRemoteConnectionAsync(RemoteSystem remotesys)
        {
            if (remotesys != null)
            {
                // Create a remote system connection request.
                RemoteSystemConnectionRequest connectionRequest = new RemoteSystemConnectionRequest(remotesys);

                this.LoadingBar.IsEnabled = true;
                this.LoadingBar.Visibility = Visibility.Visible;
                NotifyUser("Sharing to " + remotesys.DisplayName + "...", NotifyType.StatusMessage);
                var status = await RemoteLauncher.LaunchUriAsync(connectionRequest, new Uri("share-app:?Text=" + this.ClipboardText.Text));
                NotifyUser(status.ToString(), NotifyType.StatusMessage);
                this.LoadingBar.IsEnabled = false;
                this.LoadingBar.Visibility = Visibility.Collapsed;
            }
            else
            {
                NotifyUser("Select a device for remote connection.", NotifyType.ErrorMessage);
            }
        }
        public void NotifyUser(string strMessage, NotifyType type)
        {
            StatusBlock.Text = strMessage;
        }

        private void ClipboardText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (this.ClipboardText.Text.Length > 0 && this.DeviceListBox.SelectedItem != null)
            {
                if (this.ClipboardText.Text.ToLower().StartsWith("http://") || this.ClipboardText.Text.ToLower().StartsWith("https://"))
                {
                    this.LaunchInBrowserButton.IsEnabled = true;
                }
                this.CopyToClipboardButton.IsEnabled = true;
            }
            else
            {
                this.LaunchInBrowserButton.IsEnabled = false;
                this.CopyToClipboardButton.IsEnabled = false;
            }
        }

        private void DeviceListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (this.ClipboardText.Text.Length > 0 && this.DeviceListBox.SelectedItem != null)
            {
                if (this.ClipboardText.Text.ToLower().StartsWith("http://") || this.ClipboardText.Text.ToLower().StartsWith("https://"))
                {
                    this.LaunchInBrowserButton.IsEnabled = true;
                }
                this.CopyToClipboardButton.IsEnabled = true;
            }
            else
            {
                this.LaunchInBrowserButton.IsEnabled = false;
                this.CopyToClipboardButton.IsEnabled = false;
            }
        }

        private async void LaunchInBrowserButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedDevice = this.DeviceListBox.SelectedItem as RemoteSystem;

            if (selectedDevice != null)
            {
                Uri uri;
                if (Uri.TryCreate(this.ClipboardText.Text, UriKind.Absolute, out uri))
                {
                    this.LoadingBar.IsEnabled = true;
                    this.LoadingBar.Visibility = Visibility.Visible;
                    NotifyUser("Sharing to " + selectedDevice.DisplayName + "...", NotifyType.StatusMessage);
                    RemoteLaunchUriStatus launchUriStatus = await RemoteLauncher.LaunchUriAsync(new RemoteSystemConnectionRequest(selectedDevice), uri);
                    NotifyUser(launchUriStatus.ToString(), NotifyType.StatusMessage);
                    this.LoadingBar.IsEnabled = false;
                    this.LoadingBar.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void CopyToClipboardButton_Click(object sender, RoutedEventArgs e)
        {
            RemoteSystem selectedDevice = this.DeviceListBox.SelectedItem as RemoteSystem;
            this.openRemoteConnectionAsync(selectedDevice);
        }

        private void CopyToLocalClipboardButton_Click(object sender, RoutedEventArgs e)
        {
            DataPackage package = new DataPackage()
            {
                RequestedOperation = DataPackageOperation.Copy
            };
            package.SetText(this.ClipboardText.Text);
            Clipboard.SetContent(package);
            Clipboard.Flush();
            this.CopyToLocalClipboardButton.Visibility = Visibility.Collapsed;
            this.CopyToLocalClipboardButton.IsEnabled = true;
            NotifyUser("Copied!", NotifyType.StatusMessage);
        }

        private void LaunchInBrowserButton_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            var button = sender as Button;
            this.animateButtonEnabled(button);
        }

        private void CopyToClipboardButton_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            var button = sender as Button;
            this.animateButtonEnabled(button);
        }

        private void CopyToLocalClipboardButton_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            var button = sender as Button;
            this.animateButtonEnabled(button);
        }

        private void animateButtonEnabled(Button button)
        {
            var itemVisual = ElementCompositionPreview.GetElementVisual(button);

            Vector3KeyFrameAnimation scaleAnimation = _compositor.CreateVector3KeyFrameAnimation();
            scaleAnimation.Duration = TimeSpan.FromMilliseconds(1000);
            scaleAnimation.InsertKeyFrame(0f, new Vector3(1f, 1f, 1f));

            if (button.IsEnabled)
            {
                scaleAnimation.InsertKeyFrame(0.1f, new Vector3(1.2f, 1.2f, 1.2f));
            }
            else
            {
                scaleAnimation.InsertKeyFrame(0.1f, new Vector3(0.8f, 0.8f, 0.8f));
            }

            scaleAnimation.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f));
            itemVisual.StartAnimation("Scale", scaleAnimation);
        }

        private void animateListView()
        {
            var itemVisual = ElementCompositionPreview.GetElementVisual(this.DeviceListBox);

            Vector3KeyFrameAnimation scaleAnimation = _compositor.CreateVector3KeyFrameAnimation();
            scaleAnimation.InsertKeyFrame(0f, new Vector3(1f, 1f, 1f));
            scaleAnimation.InsertKeyFrame(0.1f, new Vector3(0.8f, 0.8f, 0.8f));
            scaleAnimation.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f));
            scaleAnimation.Duration = TimeSpan.FromMilliseconds(1000);

            itemVisual.StartAnimation("Scale", scaleAnimation);
        }
    }
    public enum NotifyType
    {
        StatusMessage,
        ErrorMessage
    };

    sealed partial class App : Application
    {
        protected override void OnShareTargetActivated(ShareTargetActivatedEventArgs args)
        {
            var rootFrame = CreateRootFrame();
            rootFrame.Navigate(typeof(ShareWebLink), args.ShareOperation);
            Window.Current.Activate();
        }
    }
}
