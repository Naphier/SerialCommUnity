/**
 * SerialCommUnity (Serial Communication for Unity)
 * Author: Daniel Wilches <dwilches@gmail.com>
 *
 * This work is released under the Creative Commons Attributions license.
 * https://creativecommons.org/licenses/by/2.0/
 */

using UnityEngine;

using System;
using System.IO;
using System.IO.Ports;
using System.Collections;
using System.Threading;

/**
 * This class contains methods that must be run from inside a thread and others
 * that must be invoked from Unity. Both types of methods are clearly marked in
 * the code, although you, the final user of this library, don't need to even
 * open this file unless you are introducing incompatibilities for upcoming
 * versions.
 */
public abstract class AbstractSerialThread
{
	// Constants used to mark the start and end of a connection. There is no
	// way you can generate clashing messages from your serial device, as I
	// compare the references of these strings, no their contents. So if you
	// send these same strings from the serial device, upon reconstruction they
	// will have different reference ids.
	public const string SERIAL_DEVICE_CONNECTED = "__Connected__";
	public const string SERIAL_DEVICE_DISCONNECTED = "__Disconnected__";


	// Parameters passed from SerialController, used for connecting to the
	// serial device as explained in the SerialController documentation.
	private string portName;
    private int baudRate;
    private int delayBeforeReconnecting;
    private int maxUnreadMessages;
	private Parity parity = Parity.None;
	private StopBits stopBits = StopBits.One;
	private int dataBits = 8;
	private int readTimeout = 100;
	private int writeTimeout = 100;
	private object newestIncomingMessage = null;
	private object newestOutgoingMessage = null;
	private int outgoingMessagePause;
	private bool sendOnlyNewest = false;

	// Object from the .Net framework used to communicate with serial devices.
	private SerialPort serialPort;

    // Internal synchronized queues used to send and receive messages from the
    // serial device. They serve as the point of communication between the
    // Unity thread and the SerialComm thread.
    private Queue inputQueue, outputQueue;

    // Indicates when this thread should stop executing. When SerialController
    // invokes 'RequestStop()' this variable is set.
    private bool stopRequested = false;

    private bool enqueueStatusMessages = false;

	public bool IsOpen
	{
		get
		{
			if (serialPort == null)
				return false;
			else
				return serialPort.IsOpen;
		}
	}

	private Thread thread;

    /**************************************************************************
     * Methods intended to be invoked from the Unity thread.
     *************************************************************************/

    // ------------------------------------------------------------------------
    // Constructs the thread object. This object is not a thread actually, but
    // its method 'RunForever' can later be used to create a real Thread.
    // ------------------------------------------------------------------------
    public AbstractSerialThread(string portName,
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
    {
        this.portName = portName;
        this.baudRate = baudRate;
        this.delayBeforeReconnecting = delayBeforeReconnecting;
        this.maxUnreadMessages = maxUnreadMessages;
        this.enqueueStatusMessages = enqueueStatusMessages;

		this.parity = parity;
		this.stopBits = stopBits;
		this.dataBits = dataBits;
		this.readTimeout = readTimeout;
		this.writeTimeout = writeTimeout;
		this.outgoingMessagePause = outgoingMessagePause;
		this.sendOnlyNewest = sendOnlyNewest;
		Init();
    }


	public AbstractSerialThread(SerialPort serialPort, 
								int delayBeforeReconnecting = 1000, 
								int maxUnreadMessages = 1, 
								bool enqueueStatusMessages = true)
	{
		this.serialPort = serialPort;
		portName = serialPort.PortName;
		baudRate = serialPort.BaudRate;
		parity = serialPort.Parity;
		stopBits = serialPort.StopBits;
		dataBits = serialPort.DataBits;
		readTimeout = serialPort.ReadTimeout;
		writeTimeout = serialPort.WriteTimeout;

		this.delayBeforeReconnecting = delayBeforeReconnecting;
		this.maxUnreadMessages = maxUnreadMessages;
		this.enqueueStatusMessages = enqueueStatusMessages;

		Init();
	}


	private void Init()
	{
		thread = new Thread(new ThreadStart(RunForever));
		inputQueue = Queue.Synchronized(new Queue());
		outputQueue = Queue.Synchronized(new Queue());
	}

    // ------------------------------------------------------------------------
    // Invoked to indicate to this thread object that it should stop.
    // ------------------------------------------------------------------------
	public void ShutDown()
	{
		lock (this)
		{
			stopRequested = true;
		}
	}

	// ------------------------------------------------------------------------
	// Polls the internal message queue returning the next available message
	// in a generic form. This can be invoked by subclasses to change the
	// type of the returned object.
	// It returns null if no message has arrived since the latest invocation.
	// ------------------------------------------------------------------------
	public object ReadMessage()
	{
		if (maxUnreadMessages > 0)
		{
			if (inputQueue.Count == 0)
				return null;

			return inputQueue.Dequeue();
		}
		else
			return newestIncomingMessage;
    }

    // ------------------------------------------------------------------------
    // Schedules a message to be sent. It writes the message to the
    // output queue, later the method 'RunOnce' reads this queue and sends
    // the message to the serial device.
    // ------------------------------------------------------------------------
    public void SendMessage(object message)
    {
		newestOutgoingMessage = message;
        outputQueue.Enqueue(message);
    }

    /**************************************************************************
     * Methods intended to be invoked from the SerialComm thread (the one
     * created by the SerialController).
     *************************************************************************/

    // ------------------------------------------------------------------------
    // Enters an almost infinite loop of attempting connection to the serial
    // device, reading messages and sending messages. This loop can be stopped
    // by invoking 'RequestStop'.
    // ------------------------------------------------------------------------
    public void RunForever()
    {
        // This 'try' is for having a log message in case of an unexpected
        // exception.
        try
        {
            while (!IsStopRequested())
            {
                try
                {
                    AttemptConnection();

					// Enter the semi-infinite loop of reading/writing to the
					// device.
					while (!IsStopRequested())
					{
						RunOnce();
						if (outgoingMessagePause > 0)
							Thread.Sleep(outgoingMessagePause);
					}
                }
                catch (Exception ioe)
                {
                    // A disconnection happened, or there was a problem
                    // reading/writing to the device. Log the detailed message
                    // to the console and notify the listener.
                    Debug.LogWarning("Exception: " + ioe.Message + " StackTrace: " + ioe.StackTrace);
                    if (enqueueStatusMessages)
                        inputQueue.Enqueue(SERIAL_DEVICE_DISCONNECTED);

                    // As I don't know in which stage the SerialPort threw the
                    // exception I call this method that is very safe in
                    // disregard of the port's status
                    CloseDevice();

                    // Don't attempt to reconnect just yet, wait some
                    // user-defined time. It is OK to sleep here as this is not
                    // Unity's thread, this doesn't affect frame-rate
                    // throughput.
                    Thread.Sleep(delayBeforeReconnecting);
                }
            }

			// Before closing the COM port, give the opportunity for all messages
			// from the output queue to reach the other endpoint.
			if (sendOnlyNewest && serialPort != null && newestOutgoingMessage != null)
			{
				SendToWire(newestOutgoingMessage, serialPort);
				newestOutgoingMessage = null;
			}
			else
			{
				while (outputQueue.Count != 0 && serialPort != null)
				{
					SendToWire(outputQueue.Dequeue(), serialPort);
				}
			}
            // Attempt to do a final cleanup. This method doesn't fail even if
            // the port is in an invalid status.
            CloseDevice();
        }
        catch (Exception e)
        {
            Debug.LogError("Unknown exception: " + e.Message + "\n" + e.StackTrace);
        }

		if (thread != null)
		{
			thread.Join();
			thread = null;
		}
	}

    // ------------------------------------------------------------------------
    // Try to connect to the serial device. May throw IO exceptions.
    // ------------------------------------------------------------------------
    private void AttemptConnection()
    {
		portName = portName.ToUpperInvariant();
		if (!portName.Contains("COM"))
		{
			Debug.LogError("Invlid port name. Should be in the format of 'COM10'. Portname entered: '" + portName + "'");
			return;
		}

		if (portName.Length > 4) // handle com ports > 9
		{
			portName = @"\\.\" + portName;
		}

		try
		{
			serialPort = new SerialPort()
			{
				PortName = portName,
				BaudRate = baudRate,
				ReadTimeout = readTimeout,
				WriteTimeout = writeTimeout,
				Parity = parity,
				StopBits = stopBits,
				DataBits = dataBits
			};

			serialPort.Open();

			if (enqueueStatusMessages)
				inputQueue.Enqueue(SERIAL_DEVICE_CONNECTED);
		}
		catch (IOException ex)
		{
			Debug.LogError("Failed opening port. Exception: " + ex.Message + "\n" + ex.StackTrace);
		}
    }

    // ------------------------------------------------------------------------
    // Release any resource used, and don't fail in the attempt.
    // ------------------------------------------------------------------------
    private void CloseDevice()
    {
        if (serialPort == null)
            return;

        try
        {
            serialPort.Close();
			serialPort.Dispose();
        }
        catch (IOException)
        {
            // Nothing to do, not a big deal, don't try to cleanup any further.
        }

        serialPort = null;
    }

    // ------------------------------------------------------------------------
    // Just checks if 'RequestStop()' has already been called in this object.
    // ------------------------------------------------------------------------
    private bool IsStopRequested()
    {
        lock (this)
        {
            return stopRequested;
        }
    }

    // ------------------------------------------------------------------------
    // A single iteration of the semi-infinite loop. Attempt to read/write to
    // the serial device. If there are more lines in the queue than we may have
    // at a given time, then the newly read lines will be discarded. This is a
    // protection mechanism when the port is faster than the Unity progeram.
    // If not, we may run out of memory if the queue really fills.
    // ------------------------------------------------------------------------
    private void RunOnce()
    {
        try
        {
            // Send a message.
			if (sendOnlyNewest && newestOutgoingMessage != null)
			{
				SendToWire(newestOutgoingMessage, serialPort);
				newestOutgoingMessage = null;
			}
			else if (outputQueue.Count != 0)
            {
                SendToWire(outputQueue.Dequeue(), serialPort);
            }

            // Read a message.
            // If a line was read, and we have not filled our queue, enqueue
            // this line so it eventually reaches the Message Listener.
            // Otherwise, discard the line.
            object inputMessage = ReadFromWire(serialPort);
			if (inputMessage != null)
			{
				if (maxUnreadMessages > 0)
				{
					if (inputQueue.Count < maxUnreadMessages)
					{
						inputQueue.Enqueue(inputMessage);
					}
					else
					{
						Debug.LogWarning("Queue is full. Dropping message: " + inputMessage);
					}
				}
				else
				{
					newestIncomingMessage = inputMessage;
				}
            }
        }
        catch (TimeoutException)
        {
            // This is normal, not everytime we have a report from the serial device
        }
    }

	
	// ------------------------------------------------------------------------
	// Sends a message through the serialPort.
	// ------------------------------------------------------------------------
	protected abstract void SendToWire(object message, SerialPort serialPort);

    // ------------------------------------------------------------------------
    // Reads and returns a message from the serial port.
    // ------------------------------------------------------------------------
    protected abstract object ReadFromWire(SerialPort serialPort);
}
