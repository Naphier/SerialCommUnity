/**
 * SerialCommUnity (Serial Communication for Unity)
 * Author: Daniel Wilches <dwilches@gmail.com>
 *
 * This work is released under the Creative Commons Attributions license.
 * https://creativecommons.org/licenses/by/2.0/
 */

using UnityEngine;
using System.Threading;
using UnityEngine.Events;

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

	public UnityEvent OnConnected = null;
	public UnityEvent OnDisconnected = null;
	public class UnityStringEvent : UnityEvent<string> { }
	public UnityStringEvent OnMessageReceived = null;

	[Tooltip("Turn this on if you want to manually poll messages instead of using the above events.")]
	public bool ManuallyPollMessages = false;
	

	protected AbstractSerialThread serialThread;

	void OnEnable()
	{
		if (startOnEnabled)
			Init();
	}

	public void Init()
	{
		serialThread = new SerialThread(portName,
										baudRate,
										reconnectionDelay,
										maxUnreadMessages,
										true);
	}

	public void Init(
		AbstractSerialThread serialThread,
		bool manuallyPollMessages,
		UnityStringEvent onMessageRecieved = null,
		UnityEvent onConnected = null, UnityEvent onDisconnected = null)
	{
		ManuallyPollMessages = manuallyPollMessages;
		OnMessageReceived = onMessageRecieved;
		OnConnected = onConnected;
		OnDisconnected = onDisconnected;
	}

	void OnDisable()
	{
		Close();
	}

	public void Close()
	{
		// If there is a user-defined tear-down function, execute it before
		// closing the underlying COM port.
		if (userDefinedTearDownFunction != null)
			userDefinedTearDownFunction();

		// The serialThread reference should never be null at this point,
		// unless an Exception happened in the OnEnable(), in which case I've
		// no idea what face Unity will make.
		if (serialThread != null)
		{
			serialThread.ShutDown();
			serialThread = null;
		}
	}

	// ------------------------------------------------------------------------
	// Polls messages from the queue that the SerialThread object keeps. Once a
	// message has been polled it is removed from the queue. There are some
	// special messages that mark the start/end of the communication with the
	// device.
	// ------------------------------------------------------------------------
	void Update()
	{
		if (ManuallyPollMessages)
			return;

		// Read the next message from the queue
		string message = (string)serialThread.ReadMessage();
		if (!string.IsNullOrEmpty(message))
			return;

		// Check if the message is plain data or a connect/disconnect event.
		if (ReferenceEquals(message, AbstractSerialThread.SERIAL_DEVICE_CONNECTED) &&
			OnConnected != null)
		{
			OnConnected.Invoke();
		}
		else if (ReferenceEquals(message, AbstractSerialThread.SERIAL_DEVICE_DISCONNECTED) &&
			OnDisconnected != null)
		{
			OnDisconnected.Invoke();
		}
		else if (OnMessageReceived != null)
		{
			OnMessageReceived.Invoke(message);
		}
    }

    // ------------------------------------------------------------------------
    // Returns a new unread message from the serial device. You only need to
    // call this if you don't provide a message listener.
    // ------------------------------------------------------------------------
    public string ReadSerialMessage()
    {
        // Read the next message from the queue
        return (string)serialThread.ReadMessage();
    }

    // ------------------------------------------------------------------------
    // Puts a message in the outgoing queue. The thread object will send the
    // message to the serial device when it considers it's appropriate.
    // ------------------------------------------------------------------------
    public void SendSerialMessage(string message, bool appendNewLine = true, string newLine = "\n")
    {
		if (appendNewLine)
			message = message + newLine;

        serialThread.SendMessage(message);
    }

    // ------------------------------------------------------------------------
    // Executes a user-defined function before Unity closes the COM port, so
    // the user can send some tear-down message to the hardware reliably.
    // ------------------------------------------------------------------------
    public delegate void TearDownFunction();
    private TearDownFunction userDefinedTearDownFunction;
    public void SetTearDownFunction(TearDownFunction userFunction)
    {
        userDefinedTearDownFunction = userFunction;
    }

}
