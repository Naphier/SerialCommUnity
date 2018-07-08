using System;
using UnityEngine;

namespace UnitySerialPort
{
	/// <summary>
	/// This class handles thread saftey to dequeue actions back on Unity's main thread.
	/// Gets constructed by AbstractSerialThread. 
	/// Do not call on this manually.
	/// </summary>
	public class UnitySerialPortBehaviour : MonoBehaviour
	{
		AbstractSerialThread serialThread;
		bool manuallyPollMessages;

		public static UnitySerialPortBehaviour Instantiate(
			AbstractSerialThread serialThread, 
			bool manuallyPollMessages)
		{
			var go = new GameObject("[UnitySerialPortBehaviour]");
			var uspb = go.AddComponent<UnitySerialPortBehaviour>();
			uspb.serialThread = serialThread;
			uspb.manuallyPollMessages = manuallyPollMessages;
			return uspb;
		}

		void Update()
		{
			if (serialThread == null)
				return;

			serialThread.InvokeActions(10);

			if (!manuallyPollMessages)
				serialThread.ReadMessage();

			if (serialThread.Port == null)
				Destroy(this.gameObject);
		}
	}
}
