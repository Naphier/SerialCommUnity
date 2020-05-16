/**
 * SerialCommUnity (Serial Communication for Unity)
 * Author: Daniel Wilches <dwilches@gmail.com>
 * Heavy modifications by Sean Mann (naplandgames@gmail.com)
 * This work is released under the Creative Commons Attributions license.
 * https://creativecommons.org/licenses/by/2.0/
 */

using System.IO.Ports;

/// <summary>
/// Basic implementation for sending and recieving strings.
/// Also implements BytesPerSecondRecieved and BytesPerSecondSent which can be used to see if the 
/// serial port is being overloaded via "IsOverLoaded" property.
/// </summary>

namespace UnitySerialPort
{
	public class SerialThread : AbstractSerialThread
	{
		public SerialThread(SerialPort serialPort, ObjectDelegate onMessageReceived, 
			VoidDelegate onConnected, VoidDelegate onDisconnected, StringDelegate onError, 
			VoidDelegate onPreShutDown = null, int delayBeforeReconnecting = 1000, 
			int maxUnreadMessages = 0, int outgoingMessagePause = 0, bool sendOnlyNewest = false, 
			bool manuallyPollMessages = false) 
			:
			base(serialPort, onMessageReceived, onConnected, onDisconnected, onError, 
				onPreShutDown, delayBeforeReconnecting, maxUnreadMessages, outgoingMessagePause, 
				sendOnlyNewest, manuallyPollMessages)
		{
		}

		protected override object ReadFromWire(SerialPort serialPort)
		{
			if (serialPort == null || !serialPort.IsOpen)
				return null;

			string received = serialPort.ReadLine();
			BytesPerSecondReceived += (uint)(received.Length * sizeof(System.Char));
			return received;
			
			/*
			byte[] data = new byte[1024];
			serialPort.Read(data, 0, data.Length);

			int end = 0;
			for (int i = 0; i < data.Length; i++)
			{
				if (data[i] == 0)
				{
					end = i - 1;
					break;
				}
			}

			if (end <= 0)
			{
				return null;
			}

			byte[] output = new byte[end];
			System.Array.Copy(data, output, end);
			serialPort.DiscardInBuffer();

			return System.Text.Encoding.Default.GetString(output);
			*/
		}

		protected override void SendToWire(object message, SerialPort serialPort)
		{
			if (serialPort == null || !serialPort.IsOpen)
				return;

			string sent = (string)message;
			BytesPerSecondSent += (uint)(sent.Length * sizeof(System.Char));
			serialPort.WriteLine(sent);
		}
	}
}