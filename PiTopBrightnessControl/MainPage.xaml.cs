using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using System.Threading.Tasks;


using Windows.Devices.Spi;

using Windows.Devices.Enumeration;
using System.Diagnostics;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace PiTopBrightnessControl
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public const int MAX_tries=5;
        public const int TIME_retry = 100;
        public const byte MASK_4bit = 0x0F;
        public const byte MASK_shutdown = 0x01;
        public const byte MASK_screen = 0x02;
        public const byte MASK_parity_shutdown_screen = 0x04;
        public const byte MASK_lid = 0x04;
        public const byte MASK_brightness = 0x78;
        public const byte MASK_parity = 0x80;
        public const byte COMMAND_probe = 0xFF;
        private SpiDevice PiTopHubDevice;
        private SpiDevice NotPiTopHubDevice;
        private int testbrightness = 0;
        private DispatcherTimer timer;
        public MainPage()
        {
            this.InitializeComponent();
        }


        private async Task StartScenarioAsync()

        {

            String spiDeviceSelector = SpiDevice.GetDeviceSelector();

            IReadOnlyList<DeviceInformation> devices = await DeviceInformation.FindAllAsync(spiDeviceSelector);

            var spiBusInfo = SpiDevice.GetBusInfo(devices[0].Id);
            //this gives the platform capabilities.


            // 1 = Chip select line to use.

            var PiTopHubSetting = new SpiConnectionSettings(1);

            PiTopHubSetting.ClockFrequency = 9600;
            //PiTopHubSetting.ClockFrequency = spiBusInfo.MinClockFrequency;
            //PiTopHubSetting.ClockFrequency = 20000; //ok?
            


            // We use Mode0 to set the clock polarity and phase to: CPOL = 0, CPHA = 0.

            PiTopHubSetting.Mode = SpiMode.Mode0;

            PiTopHubSetting.DataBitLength = 8;

            // this is a dummy device we use to control the CS. By creating another device on a different CS, we can force the CS to transition before
            // accessing the PI hub

            var NotPiTopHubSetting = new SpiConnectionSettings(0);

            NotPiTopHubSetting.Mode = SpiMode.Mode0;
            NotPiTopHubSetting.DataBitLength = 8;

            // If this next line crashes with an ArgumentOutOfRangeException,

            // then the problem is that no SPI devices were found.

            //

            // If the next line crashes with Access Denied, then the problem is

            // that access to the SPI device is denied.

            //

            // The call to FromIdAsync will also crash if the settings are invalid.

            //

            // FromIdAsync produces null if there is a sharing violation on the device.

            // This will result in a NullReferenceException a few lines later.



            PiTopHubDevice = await SpiDevice.FromIdAsync(devices[0].Id, PiTopHubSetting);

            NotPiTopHubDevice = await SpiDevice.FromIdAsync(devices[0].Id, NotPiTopHubSetting);


            // initialize hub device by reading until parity is correct

            int trycounter = 0;
            while(trycounter<MAX_tries)
            {
                trycounter++;
                byte result = RWSPI(0xff);
                //Debug.WriteLine("Result:", result);
                //BitDecode(result);
                //DecodeAnswer(result);
                if (CheckParity(result)) break;
                await Task.Delay(TIME_retry);

            }

            SetScreen(false, false, 8);


            //while(true)
            //{
            //    await Task.Delay(1000);
            //}
            // Start the polling timer.

            timer = new DispatcherTimer() { Interval = TimeSpan.FromMilliseconds(1000) };

            timer.Tick += Timer_Tick;

            timer.Start();

        }

        byte ComputeState(bool screen_off, bool shutdown, int brightness)
        {

            byte toPiTopHub = 0;
            int brightness_parity=0;
            int state_parity = 0;
            if (brightness < 0) brightness = 0;
            if (brightness > 10) brightness = 10;
            for (int i= 0;i < 5;i++)
            {
                if ((brightness & 1 << i) == (1 << i))
                {
                    brightness_parity = ~ brightness_parity ;
                }
            }
            brightness_parity &= 0x01;

            toPiTopHub += (byte) (MASK_parity * brightness_parity + ((brightness & MASK_4bit) << 3));

            if (screen_off) state_parity = ~state_parity;
            if (shutdown) state_parity = ~state_parity;
            state_parity &= 0x01;

            toPiTopHub += (byte)(MASK_parity_shutdown_screen * (state_parity) + (screen_off ? MASK_screen : 0x00) + (shutdown ? MASK_shutdown : 0x00));

            return toPiTopHub;
        }


        bool CheckParity(byte answer)
        {
            int expectedparity = 0;
            for (int i = 1; i < 7; i++)
            {
                if ((answer & 1 << i) == (1 << i))
                {
                    expectedparity = ~expectedparity;
                }
            }
            expectedparity &= 0x01;
            return ((((uint)expectedparity & 0x01) == 0x01) == (((uint)answer & MASK_parity) == MASK_parity));
        }

        uint GetBrightness(byte answer)
        {
            return (((uint)answer & MASK_brightness) >> 3);

        }
        void DecodeAnswer(byte answer)
        {

            if (((uint)answer & MASK_shutdown) == MASK_shutdown)
            {
                Debug.WriteLine("shutdown");
            }
            if (((uint)answer & MASK_screen) == MASK_screen)
            {
                Debug.WriteLine("screen");
            }
            if (((uint)answer & MASK_lid) == MASK_lid)
            {
                Debug.WriteLine("lid");
            }

            Debug.WriteLine("brightness:" + GetBrightness(answer));

            
            Debug.WriteLine("");

        }
        byte NotPiRead()
            // this function is a dummy read to a different SPI device. This forces the CS of the PI Hub to be deasserted.
        {
            byte[] ReadBuf = new byte[1];

            NotPiTopHubDevice.Read( ReadBuf);

            return ReadBuf[0];
        }

        byte RWSPI(byte send)
        {
            byte[] WriteBuf = new byte[1];
            byte[] ReadBuf = new byte[1];
            WriteBuf[0] = send;
            NotPiRead();
            PiTopHubDevice.TransferFullDuplex(WriteBuf, ReadBuf);

            return ReadBuf[0];
        }

        async void SetScreen(bool screen_off, bool shutdown, int brightness)
        {

            byte result;
            byte send;

            send = ComputeState(false, false, brightness);

            result = RWSPI(send);
            while (!CheckParity(result))
            {
                //DecodeAnswer(result);
                //Debug.WriteLine("bad parity, retrying");
                await Task.Delay(TIME_retry);
                result = RWSPI(send);
            }
            int trycounter = 0;
            while(GetBrightness(result)!=brightness && trycounter <MAX_tries)
            {
                trycounter++;
                //DecodeAnswer(result);
                //Debug.WriteLine("bad brightness, retrying");
                await Task.Delay(TIME_retry);
                result = RWSPI(send);
                while (!CheckParity(result))
                {
                    //DecodeAnswer(result);
                    //Debug.WriteLine("bad parity, retrying");
                    await Task.Delay(TIME_retry);
                    result = RWSPI(send);
                }

            }
            //Debug.WriteLine("Trycounter is {0}", trycounter);
            //if(GetBrightness(result) != brightness)
            //{
            //    Debug.WriteLine("Gave up, could not set brightness successfully!");
            //}

            //DecodeAnswer(result);

        }

        void Timer_Tick(object sender, object e)

        {
            

            Debug.WriteLine("Setting Brightness to " + testbrightness);
            SetScreen(false, false, testbrightness);

            testbrightness++;
            if (testbrightness > 10) testbrightness = 0;
            
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await StartScenarioAsync();
        }
    }
}
