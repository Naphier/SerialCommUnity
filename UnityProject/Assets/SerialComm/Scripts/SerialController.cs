/**
 * SerialCommUnity (Serial Communication for Unity)
 * Author: Daniel Wilches <dwilches@gmail.com>
 * Heavy modifications by Sean Mann (naplandgames@gmail.com)
 * This work is released under the Creative Commons Attributions license.
 * https://creativecommons.org/licenses/by/2.0/
 */

using UnityEngine;
using UnityEngine.Events;
using UnitySerialPort;

/**
 * This class allows a Unity program to continually check for messages from a
 * serial device.
 *
 * It creates a Thread that communicates with the serial port and continually
 * polls the messages on the wire.
 * That Thread puts all the messages inside a Queue, and this SerialController
 * class polls that queue by means of invoking SerialThread.GetSerialMessage().
 *
 * The serial device must send its messages separated by a newline character.
 * Neither the SerialController nor the SerialThread perform any validation
 * on the integrity of the message. It's up to the one that makes sense of the
 * data.
 */
public class SerialController : MonoBehaviour
{
	[Tooltip("Should the serial port open and start when this script is enabled?" +
			 "If not then start by calling Init()")]
	[SerializeField]
	protected bool startOnEnabled = false;

	[Tooltip("Port name with which the SerialPort object will be created.")]
	[SerializeField]
	protected string portName = "COM3";

	[Tooltip("Baud rate that the serial device is using to transmit data.")]
	[SerializeField]
	protected int baudRate = 9600;

	[Tooltip("After an error in the serial communication, or an unsuccessful " +
			 "connect, how many milliseconds we should wait.")]
	[SerializeField]
	protected int reconnectionDelay = 1000;

	[Tooltip("Maximum number of unread data messages in the queue. " +
			 "New messages will be discarded. Set to 0 to only read newest message.")]
	[SerializeField]
	protected int maxUnreadMessages = 0;

	public UnityEvent OnConnected = new UnityEvent();
	public UnityEvent OnDisconnected = new UnityEvent();
	public class UnityStringEvent : UnityEvent<string> { }
	public UnityStringEvent OnMessageReceived = new UnityStringEvent();
	public UnityStringEvent OnError = new UnityStringEvent();

	[Tooltip("Turn this on if you want to manually poll messages instead of using the above events.")]
	public bool ManuallyPollMessages = false;
	

	public AbstractSerialThread SerialThread { get; private set; }

	void OnEnable()
	{
		if (startOnEnabled)
			Init();
	}

	public void Init()
	{
		SerialThread = new SerialThread(
			new System.IO.Ports.SerialPort {
				PortName = portName,
				BaudRate = baudRate,
				ReadTimeout = 1,
				WriteTimeout = 1
			},
			(msgReceivedObj) => { OnMessageReceived.Invoke((string)msgReceivedObj); },
			OnConnected.Invoke,
			OnDisconnected.Invoke,
			OnError.Invoke,
			delayBeforeReconnecting: reconnectionDelay,
			maxUnreadMessages: maxUnreadMessages, 
			manuallyPollMessages: ManuallyPollMessages);
	}

	public void Init(
		AbstractSerialThread serialThread,
		bool manuallyPollMessages)
	{
		SerialThread = serialThread;
		ManuallyPollMessages = manuallyPollMessages;
	}

	void OnDisable()
	{
		Close();
	}

	public void Close()
	{
		// The serialThread reference should never be null at this point,
		// unless an Exception happened in the OnEnable(), in which case I've
		// no idea what face Unity will make.
		if (SerialThread != null)
		{
			SerialThread.ShutDown();
		}
	}

	

    /// <summary>
	/// Manual read option if you don't provide an OnMessageReceived Listener.
	/// </summary>
	/// <returns>incoming messag as string</returns>
    public string ReadSerialMessage()
    {
        // Read the next message from the queue
        return (string)SerialThread.ReadMessage();
    }

	/// <summary>
	/// Puts a message in the outgoing queue. The thread object will send the
	/// message to the serial device when it considers it's appropriate.
	/// </summary>
	/// <param name="message"></param>
	/// <param name="appendNewLine"></param>
	/// <param name="newLine"></param>
	public void SendSerialMessage(string message, bool appendNewLine = true, string newLine = "\n")
    {
		if (appendNewLine)
			message = message + newLine;

        SerialThread.SendMessage(message);
    }
}
