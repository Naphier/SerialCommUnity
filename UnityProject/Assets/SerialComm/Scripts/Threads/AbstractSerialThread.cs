/**
 * SerialCommUnity (Serial Communication for Unity)
 * Author: Daniel Wilches <dwilches@gmail.com>
 * Heavy modifications by Sean Mann (naplandgames@gmail.com)
 * This work is released under the Creative Commons Attributions license.
 * https://creativecommons.org/licenses/by/2.0/
 */

using System;
using System.IO;
using System.IO.Ports;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace UnitySerialPort
{
	public abstract class AbstractSerialThread
	{
		public delegate void VoidDelegate();
		public delegate void ObjectDelegate(object val);
		public delegate void StringDelegate(string val);

		public VoidDelegate OnConnected;
		public VoidDelegate OnDisconnected;
		public VoidDelegate OnPreShutDown;
		public ObjectDelegate OnMessageReceived;
		public StringDelegate OnError;

		public SerialPort Port { get; private set; }

		public uint BytesPerSecondSent { get; protected set; }
		public uint BytesPerSecondReceived { get; protected set; }

		public int ReadTimeoutCount { get; private set; }
		public int WriteTimeoutCount { get; private set; }

		int reconnectDelay, maxUnreadMessages, outgoingMessagePause;
		object newestIncomingMessage, newestOutgoingMessage;
		bool sendOnlyNewest, stopRequested;

		List<Action> actionQueue;
		Queue inputQueue, outputQueue;
		Thread thread;
		Timer timer;

		public uint BytesPerSecond
		{
			get { return BytesPerSecondSent + BytesPerSecond; }
		}

		public bool IsOverLoaded
		{
			get
			{
				if (Port == null)
					return false;

				return BytesPerSecond >= Port.BaudRate / 8;
			}
		}

		public bool IsOpen
		{
			get
			{
				if (Port == null)
					return false;
				else
					return Port.IsOpen;
			}
		}

		public AbstractSerialThread(SerialPort serialPort,
									ObjectDelegate onMessageReceived,
									VoidDelegate onConnected,
									VoidDelegate onDisconnected,
									StringDelegate onError,
									VoidDelegate onPreShutDown = null,
									int delayBeforeReconnecting = 1000,
									int maxUnreadMessages = 0,
									int outgoingMessagePause = 0,
									bool sendOnlyNewest = false, 
									bool manuallyPollMessages = false)
		{
			Port = serialPort;

			if (serialPort.ReadTimeout == SerialPort.InfiniteTimeout ||
				serialPort.WriteTimeout == SerialPort.InfiniteTimeout)
			{
				throw new Exception("Serial port timeout should not be infinite.");
			}

			reconnectDelay = delayBeforeReconnecting;
			this.maxUnreadMessages = maxUnreadMessages;
			this.outgoingMessagePause = outgoingMessagePause;
			this.sendOnlyNewest = sendOnlyNewest;

			OnMessageReceived += (obj)=> { };
			OnConnected += () => { };
			OnDisconnected += () => { };
			OnError += (msg) => { };
			OnPreShutDown += () => { };

			OnMessageReceived += onMessageReceived;
			OnConnected += onConnected;
			OnDisconnected += onDisconnected;
			OnError += onError;
			OnPreShutDown += onPreShutDown;

			UnitySerialPortBehaviour.Instantiate(this, manuallyPollMessages);

			thread = new Thread(new ThreadStart(RunForever));
			thread.Start();
			inputQueue = Queue.Synchronized(new Queue());
			outputQueue = Queue.Synchronized(new Queue());
			actionQueue = new List<Action>();
		}

		public void InvokeActions(int maxPerFrame = int.MaxValue)
		{
			int count = maxPerFrame;
			if (actionQueue.Count < count)
				count = actionQueue.Count;

			for (int i = 0; i < count; i++)
			{
				actionQueue[i]();
			}

			actionQueue.RemoveRange(0, count);
		}

		/// <summary>
		/// Call this to stop the thread and close the port.
		/// </summary>
		public void ShutDown()
		{
			lock (this)
			{
				stopRequested = true;
			}
		}


		/// <summary>
		/// Read a message from the port.
		/// </summary>
		public object ReadMessage()
		{
			object msg = null;

			if (maxUnreadMessages > 0)
			{
				if (inputQueue.Count == 0)
				{
					OnMessageReceived.Invoke(null);
					return null;
				}
				else
				{
					msg = inputQueue.Dequeue();
					OnMessageReceived(msg);
				}
			}
			else
			{
				OnMessageReceived(newestIncomingMessage);
				msg = newestIncomingMessage;
				newestIncomingMessage = null;
			}
			return msg;
		}


		/// <summary>
		/// Schedules a message to be sent. It writes the message to the
		/// output queue, later the method 'RunOnce' reads this queue and sends
		/// </summary>
		/// the message to the serial device.<param name="message"></param>
		public void SendMessage(object message)
		{
			newestOutgoingMessage = message;
			outputQueue.Enqueue(message);
		}

		/// <summary>
		/// Continous thread to handle outgoing and incoming message queues.
		/// Will stop when RequestStop() is called.
		/// </summary>
		void RunForever()
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
						actionQueue.Add(() =>
						{
							OnError.Invoke(
								"Exception: " + ioe.Message + 
								" StackTrace: " + ioe.StackTrace);
						});
						CloseDevice();

						// Wait to reconnect.
						Thread.Sleep(reconnectDelay);
					}
				}

				// Send remaining outgoing messages.
				if (sendOnlyNewest && Port != null && newestOutgoingMessage != null)
				{
					SendToWire(newestOutgoingMessage, Port);
					newestOutgoingMessage = null;
				}
				else
				{
					while (outputQueue.Count != 0 && Port != null)
					{
						SendToWire(outputQueue.Dequeue(), Port);
					}
				}
				
				CloseDevice();
			}
			catch (Exception e)
			{
				actionQueue.Add(() =>
				{
					OnError.Invoke("Unknown exception: " + e.Message + "\n" + e.StackTrace);
				});
			}

			if (thread != null)
			{
				thread.Join(100); // Using a short timeout because this likes to get hung up.
				thread = null;
			}
		}

		/// <summary>
		/// Corrects port names for COM port 10 and above. Because .NET can't handle it...
		/// </summary>
		/// <param name="portName"></param>
		/// <returns></returns>
		string GetCorrectedPortName(string portName)
		{
			portName = portName.ToUpperInvariant();
			if (!portName.Contains("COM"))
			{
				throw new Exception("Invlid port name. Should be in the format of 'COM10'. Portname entered: '" + portName + "'");
			}

			if (portName.Length > 4) // handle com ports > 9
			{
				portName = @"\\.\" + portName;

			}

			return portName;
		}


		void AttemptConnection()
		{
			try
			{
				Port.PortName = GetCorrectedPortName(Port.PortName);
				Port.Open();
				actionQueue.Add(OnConnected.Invoke);
				BytesPerSecondReceived = BytesPerSecondSent = 0;

				if (timer != null)
					timer.Dispose();

				timer = new Timer(OnTimerReset, null, Timeout.Infinite, 1000);
				ReadTimeoutCount = WriteTimeoutCount = 0;
			}
			catch (IOException ex)
			{
				actionQueue.Add(() =>
				{
					OnError.Invoke(
						"Failed opening port. Exception: " + ex.Message + "\n" + ex.StackTrace);
				});
			}
		}

		void OnTimerReset(object state)
		{
			BytesPerSecondReceived = BytesPerSecondSent = 0;
		}

		void CloseDevice()
		{
			if (Port == null)
				return;

			actionQueue.Add(() =>
			{ 
				OnPreShutDown.Invoke();
				try
				{
					Port.Close();
					Port.Dispose();
				}
				catch (IOException)
				{
					// Nothing to do, not a big deal, don't try to cleanup any further.
				}

				actionQueue.Add(
					() => {
						OnDisconnected.Invoke();
						Port = null;
					});
			});
		}

		bool IsStopRequested()
		{
			lock (this)
			{
				return stopRequested;
			}
		}

		private void RunOnce()
		{
			try
			{
				if (sendOnlyNewest && newestOutgoingMessage != null)
				{
					SendToWire(newestOutgoingMessage, Port);
					newestOutgoingMessage = null;
				}
				else if (outputQueue.Count != 0)
				{
					SendToWire(outputQueue.Dequeue(), Port);
				}
			}
			catch (TimeoutException)
			{
				WriteTimeoutCount++;
			}

			try
			{
				object inputMessage = ReadFromWire(Port);
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
							actionQueue.Add(() =>
							{
								OnError("Queue is full. Dropping message: " + inputMessage);
							});
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
				ReadTimeoutCount++;
			}
		}

		// Abstract methods for the actual read/write from the serial port.
		// i.e. SerialPort.Write/WriteLine or SerialPort.Read(many variations)
		protected abstract void SendToWire(object message, SerialPort serialPort);
		protected abstract object ReadFromWire(SerialPort serialPort);
	}
}
