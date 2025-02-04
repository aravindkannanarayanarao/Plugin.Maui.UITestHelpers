using NUnit.Framework;
using Plugin.Maui.UITestHelpers.Appium;
using Plugin.Maui.UITestHelpers.Core;
using System.Diagnostics;
using UITest.Appium.NUnit;
using VisualTestUtils;
using VisualTestUtils.MagickNet;
using System.Reflection;

namespace UITests.Shared;

#if ANDROID
[TestFixture(TestDevice.Android)]
#elif IOS
[TestFixture(TestDevice.iOS)]
#elif MACOS
[TestFixture(TestDevice.Mac)]
#elif WINDOWS
[TestFixture(TestDevice.Windows)]
#endif
public abstract class BaseTest : UITestBase
{
    readonly VisualRegressionTester _visualRegressionTester;
    readonly IImageEditorFactory _imageEditorFactory;
    readonly VisualTestContext _visualTestContext;
    public BaseTest(TestDevice testDevice) : base(testDevice)
    {
        string? ciArtifactsDirectory = Environment.GetEnvironmentVariable("BUILD_ARTIFACTSTAGINGDIRECTORY");
        if (ciArtifactsDirectory != null)
            ciArtifactsDirectory = Path.Combine(ciArtifactsDirectory, "Controls.TestCases.Shared.Tests");

        string projectRootDirectory = Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory)!;

        _visualRegressionTester = new VisualRegressionTester(testRootDirectory: projectRootDirectory,
            visualComparer: new MagickNetVisualComparer(),
            visualDiffGenerator: new MagickNetVisualDiffGenerator(),
            ciArtifactsDirectory: ciArtifactsDirectory);

        _imageEditorFactory = new MagickNetImageEditorFactory();
        _visualTestContext = new VisualTestContext();
    }

    public override IConfig GetTestConfig()
    {
        var config = new Config();

        var appIdentifierKey = "AppId";

        // Note: an app with this ID has to be deployed to the emulator/device you want to run it on
        var appIdentifier = "com.companyname.sample";
        var AppMain1 = "AppMain";
        var AppMain12 = "crc64b28577ed8416fd3b";

        config.SetProperty(appIdentifierKey, appIdentifier);
        config.SetProperty(AppMain1, AppMain12);

        if (_testDevice == TestDevice.Windows)
        {
            var appIdentifierKey11 = "AppName";

            // Note: a release build has to be done and the path to this .exe file should exist. Tweak this path if necessary
            var appIdentifier1 = "sample";

            config.SetProperty(appIdentifierKey11, appIdentifier1);
        }

        // If the app ID is provided through an environment variable, like through CI, use that instead
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("APPID")))
        {
            appIdentifier = Environment.GetEnvironmentVariable("APPID");
        }



        //config.SetProperty(appIdentifierKey, appIdentifier);

        if (_testDevice == TestDevice.iOS)
        {
            var appIdentifierKey11 = "AppIdiOS";

            // Note: a release build has to be done and the path to this .exe file should exist. Tweak this path if necessary
            var appIdentifier1 = "com.companyname.sample";

            config.SetProperty("DeviceName", "iPhone 15 Pro");
                config.SetProperty("PlatformVersion", "17.2");
                
        }

        return config;
    }

    public void VerifyScreenshot(string? name = null, TimeSpan? retryDelay = null)
    {
        retryDelay ??= TimeSpan.FromMilliseconds(500);
        // Retry the verification once in case the app is in a transient state
        try
        {
            Verify(name);
        }
        catch
        {
            Thread.Sleep(retryDelay.Value);
            Verify(name);
        }

        void Verify(string? name)
        {
            string deviceName = GetTestConfig().GetProperty<string>("DeviceName") ?? string.Empty;

            // Remove the XHarness suffix if present
            deviceName = deviceName.Replace(" - created by XHarness", "", StringComparison.Ordinal);

            /*
            Determine the environmentName, used as the directory name for visual testing snaphots. Here are the rules/conventions:
            - Names are lower case, no spaces.
            - By default, the name matches the platform (android, ios, windows, or mac).
            - Each platform has a default device (or set of devices) - if the snapshot matches the default no suffix is needed (e.g. just ios).
            - If tests are run on secondary devices that produce different snapshots, the device name is used as suffix (e.g. ios-iphonex).
            - If tests are run on secondary devices with multiple OS versions that produce different snapshots, both device name and os version are
            used as a suffix (e.g. ios-iphonex-16_4). We don't have any cases of this today but may eventually. The device name comes first here,
            before os version, because most visual testing differences come from different sceen size (a device thing), not OS version differences,
            but both can happen.
            */
            string environmentName = "";
            
            name ??= TestContext.CurrentContext.Test.MethodName ?? TestContext.CurrentContext.Test.Name;

            // Currently Android is the OS with the ripple animations, but Windows may also have some animations
            // that need to finish before taking a screenshot.
            if (_testDevice == TestDevice.Android)
            {
                Thread.Sleep(350);
            }

#if MACUITEST
				byte[] screenshotPngBytes = TakeScreenshot() ?? throw new InvalidOperationException("Failed to get screenshot");
#else
            byte[] screenshotPngBytes = App.Screenshot() ?? throw new InvalidOperationException("Failed to get screenshot");
#endif

            var actualImage = new ImageSnapshot(screenshotPngBytes, ImageSnapshotFormat.PNG);

            // For Android and iOS, crop off the OS status bar at the top since it's not part of the
            // app itself and contains the time, which always changes. For WinUI, crop off the title
            // bar at the top as it varies slightly based on OS theme and is also not part of the app.
            int cropFromTop = _testDevice switch
            {
                TestDevice.Android => 125,
                TestDevice.iOS => environmentName == "ios-iphonex" ? 90 : 110,
                TestDevice.Windows => 32,
                _ => 0,
            };

            // For Android also crop the 3 button nav from the bottom, since it's not part of the
            // app itself and the button color can vary (the buttons change clear briefly when tapped).
            // For iOS, crop the home indicator at the bottom.
            int cropFromBottom = _testDevice switch
            {
                TestDevice.Android => 125,
                TestDevice.iOS => 40,
                _ => 0,
            };

            if (cropFromTop > 0 || cropFromBottom > 0)
            {
                IImageEditor imageEditor = _imageEditorFactory.CreateImageEditor(actualImage);
                (int width, int height) = imageEditor.GetSize();

                imageEditor.Crop(0, cropFromTop, width, height - cropFromTop - cropFromBottom);

                actualImage = imageEditor.GetUpdatedImage();
            }

            _visualRegressionTester.VerifyMatchesSnapshot(name!, actualImage, environmentName: environmentName, testContext: _visualTestContext);
        }



#if MACUITEST
		byte[] TakeScreenshot()
		{
			// Since the Appium screenshot on Mac (unlike Windows) is of the entire screen, not just the app,
			// we are going to maximize the App before take the screenshot.
			App.EnterFullScreen();

			// The app might not be ready to take the screenshot.
			// Wait a little bit to complete the system animation moving the App Window to FullScreen.
			Thread.Sleep(500);

			byte[] screenshotPngBytes = App.Screenshot() ?? throw new InvalidOperationException("Failed to get screenshot");

			// TODO: After take the screenshot, restore the App Window to the previous state.
			App.ExitFullScreen();

			// Wait a little bit to complete the system animation moving the App Window to previous state.
			Thread.Sleep(500);

			return screenshotPngBytes;
		}
#elif WINDOWS
        App.EnterFullScreen();
#endif
    }
}