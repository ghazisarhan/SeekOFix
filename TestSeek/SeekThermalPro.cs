/*
Copyright (c) 2014 Stephen Stair (sgstair@akkit.org)
Additional code Miguel Parra (miguelvp@msn.com)

Permission is hereby granted, free of charge, to any person obtaining a copy
 of this software and associated documentation files (the "Software"), to deal
 in the Software without restriction, including without limitation the rights
 to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 copies of the Software, and to permit persons to whom the Software is
 furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
 all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 THE SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using winusbdotnet;

namespace winusbdotnet.UsbDevices
{
    /*public class CalibratedThermalFrame
    {
        public readonly int Width, Height;
        public readonly UInt16[] PixelData;
        public UInt16 MinValue;
        public UInt16 MaxValue;
    }*/

    public class ThermalProFrame
    {
        public readonly int Width, Height;
        public readonly byte[] RawData;
        public readonly UInt16[] RawDataU16;
        public readonly bool IsCalibrationFrame;
        public readonly bool IsUsableFrame;
        public readonly byte StatusByte;
        public readonly UInt16 StatusWord;
        public readonly UInt16 AvgValue;
        public readonly UInt16 FrameCounter;

        internal ThermalProFrame(Byte[] data)
        {
            Width = 342;
            Height = 260;
            RawData = data;
            StatusWord = BitConverter.ToUInt16(data, 4);
            FrameCounter = BitConverter.ToUInt16(data, 2);

            IsCalibrationFrame = StatusWord == 1;
            IsUsableFrame = StatusWord == 3;

            // Convert to 16 bit as well for easier manipulation of data.
            RawDataU16 = new UInt16[data.Length / 2];

            for (int i = 0; i < (data.Length / 2); i++)
            {
                UInt16 v = (UInt16)BitConverter.ToInt16(data, i * 2);
                RawDataU16[i] = v;
            }

        }
    }

    public class SeekThermalPro
    {
        public static IEnumerable<WinUSBEnumeratedDevice> Enumerate()
        {
            foreach (WinUSBEnumeratedDevice dev in WinUSBDevice.EnumerateAllDevices())
            {
                // Seek Thermal "iAP Interface" device - Use Zadig to install winusb driver on it.
                if (dev.VendorID == 0x289D && dev.ProductID == 0x0011 && dev.UsbInterface == 0)
                {
                    yield return dev;
                }
            }
        }

        WinUSBDevice device;

        public SeekThermalPro(WinUSBEnumeratedDevice dev)
        {
            device = new WinUSBDevice(dev);

            // device setup sequence
            try
            {
                device.ControlTransferOut(0x41, 84, 0, 0, new byte[] { 0x01 });
            }
            catch
            {
                // Try deinit device and repeat.
                Deinit();
                device.ControlTransferOut(0x41, 84, 0, 0, new byte[] { 0x01 });
            }

            device.ControlTransferOut(0x41, 60, 0, 0, new byte[] { 0x00, 0x00 });

            byte[] data1 = device.ControlTransferIn(0xC1, 78, 0, 0, 4);

            byte[] data2 = device.ControlTransferIn(0xC1, 54, 0, 0, 12);

            // Analysis of 0x56 payload: 
            // First byte seems to be half the size of the output data.
            // It seems like this command may be retriving some sensor data?
            device.ControlTransferOut(0x41, 86, 0, 0, new byte[] { 0x06, 0x00, 0x08, 0x00, 0x00, 0x00 });

            byte[] data3 = device.ControlTransferIn(0xC1, 0x58, 0, 0, 12);

            device.ControlTransferOut(0x41, 85, 0, 0, new byte[] { 0x17, 0x00 });

            byte[] data4 = device.ControlTransferIn(0xC1, 78, 0, 0, 0x40);

            device.ControlTransferOut(0x41, 86, 0, 0, new byte[] { 0x0C, 0x00, 0x70, 0x00, 0x00, 0x00 });

            byte[] data5 = device.ControlTransferIn(0xC1, 0x58, 0, 0, 2);

            device.ControlTransferOut(0x41, 86, 0, 0, new byte[] { 0x01, 0x00, 0x01, 0x06, 0x00, 0x00 });

            byte[] data6 = device.ControlTransferIn(0xC1, 88, 0, 0, 2);


            UInt16 addr;

            for (addr = 0; addr < 2560; addr += 32)
            {
                byte[] addrle_p = BitConverter.GetBytes(addr);
                device.ControlTransferOut(0x41, 86, 0, 0, new byte[] { 0x20, 0x00, addrle_p[0], addrle_p[1], 0x00, 0x00 });

                byte[] data7 = device.ControlTransferIn(0xC1, 88, 0, 0, 64);
            }

            device.ControlTransferOut(0x41, 85, 0, 0, new byte[] { 0x15, 0x00 });

            byte[] data8 = device.ControlTransferIn(0xC1, 78, 0, 0, 64);

            device.ControlTransferOut(0x41, 62, 0, 0, new byte[] { 0x08, 0x00 });

            device.ControlTransferOut(0x41, 60, 0, 0, new byte[] { 0x01, 0x00 });
        }

        // 
        public void Deinit()
        {
            device.ControlTransferOut(0x41, 60, 0, 0, new byte[] { 0x00, 0x00 });
            device.ControlTransferOut(0x41, 60, 0, 0, new byte[] { 0x00, 0x00 });
            device.ControlTransferOut(0x41, 60, 0, 0, new byte[] { 0x00, 0x00 });
        }

        public ThermalProFrame GetFrameBlocking()
        {
            Int32 size = 342 * 260;
            byte[] sizele_p = BitConverter.GetBytes(size);
            // Request frame (vendor interface request 0x53; data "C0 7e 00 00" which is half the size of the return data)
            device.ControlTransferOut(0x41, 83, 0, 0, new byte[] { sizele_p[0], sizele_p[1], sizele_p[2], sizele_p[3] });

            // Read data from IN 1 pipe
            return new ThermalProFrame(device.ReadExactPipe(0x81, size * 2));
        }
    }
}
