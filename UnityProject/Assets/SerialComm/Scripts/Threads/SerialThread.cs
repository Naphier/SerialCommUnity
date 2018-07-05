/**
 * SerialCommUnity (Serial Communication for Unity)
 * Author: Daniel Wilches <dwilches@gmail.com>
 *
 * This work is released under the Creative Commons Attributions license.
 * https://creativecommons.org/licenses/by/2.0/
 */

using UnityEngine;

using System.IO.Ports;

/// <summary>
/// Basic implementation for sending and recieving strings.
/// </summary>
public class SerialThread : AbstractSerialThread
{
	public SerialThread(string portName,
						int baudRate,
						int delayBeforeReconnecting,
						int maxUnreadMessages,
						bool enqueueStatusMessages,
						Parity parity = Parity.None,
						StopBits stopBits = StopBits.One,
						int dataBits = 8,
						int readTimeout = 100,
						int writeTimeout = 100,
						int outgoingMessagePause = 1,
						bool sendOnlyNewest = false)
		: base (portName,
						baudRate,
						delayBeforeReconnecting,
						maxUnreadMessages,
						enqueueStatusMessages,
						parity = Parity.None,
						stopBits = StopBits.One,
						dataBits = 8,
						readTimeout = 100,
						writeTimeout = 100,
						outgoingMessagePause = 1,
						sendOnlyNewest = false)
	{ }
		

		public SerialThread(SerialPort serialPort,
						int delayBeforeReconnecting = 1000,
						int maxUnreadMessages = 1,
						bool enqueueStatusMessages = true) 
		: base(serialPort, delayBeforeReconnecting, maxUnreadMessages, enqueueStatusMessages)
	{ }

	
	protected override object ReadFromWire(SerialPort serialPort)
	{
		return serialPort.ReadLine();
	}

	protected override void SendToWire(object message, SerialPort serialPort)
	{
		serialPort.WriteLine((string)message);
	}
}
