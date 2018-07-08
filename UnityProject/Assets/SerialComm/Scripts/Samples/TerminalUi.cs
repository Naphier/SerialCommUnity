using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.IO.Ports;

namespace UnitySerialPort
{
	public class TerminalUi : MonoBehaviour
	{
		[SerializeField]
		Text status;

		[SerializeField]
		SerialController serialController;

		[SerializeField]
		TerminalLineUi receivedTerminal;

		[SerializeField]
		TerminalLineUi sentTerminal;

		[SerializeField]
		Button connectDisconnectButton;

		[SerializeField]
		Text connectButtonText;

		[SerializeField]
		GameObject connectionUi;

		[SerializeField]
		Button connectUiConnectButton;

		[SerializeField]
		Dropdown baudDropDown;

		[SerializeField]
		Button sendButton;

		[SerializeField]
		InputField sendInputField;

		[SerializeField]
		Dropdown comPortDropdown;

		string selectedComPort;
		int selectedBaudRate = 2400;

		void Awake()
		{
			// Set initial states for UI elements
			connectionUi.SetActive(false);
			SetConnectButtonStateDisconnected();
			sendButton.interactable = false;

			// Restore last used baud.
			selectedBaudRate = PlayerPrefs.GetInt("BAUD", 2400);
			for (int i = 0; i < baudDropDown.options.Count; i++)
			{
				if (baudDropDown.options[i].text == selectedBaudRate.ToString())
				{
					baudDropDown.value = i;
					break;
				}
			}

			// Set up listeners for dropdowns.
			comPortDropdown.onValueChanged.AddListener((index) =>
			{
				selectedComPort = comPortDropdown.options[index].text;
				PlayerPrefs.SetString("COM", selectedComPort);
			});

			baudDropDown.onValueChanged.AddListener((index) => 
			{
				if (!int.TryParse(baudDropDown.options[index].text, out selectedBaudRate))
				{
					Debug.LogError(
						"Could not convert baud rate '" + 
						baudDropDown.options[index].text + "'to integer value.");
				}

				PlayerPrefs.SetInt("BAUD", selectedBaudRate);
			});
			
			// Set up listener for connecting
			connectUiConnectButton.onClick.AddListener(() =>
			{
				var serial = new SerialThread(
						new SerialPort()
						{
							PortName = selectedComPort,
							BaudRate = selectedBaudRate,
							WriteTimeout = 1,
							ReadTimeout = 1
						},
						(obj) => {
							string msg = (string)obj;
							if (!string.IsNullOrEmpty(msg))
							{
								receivedTerminal.AddLine(msg);
								receivedTerminal.scrollRect.verticalNormalizedPosition = 0;
							}
						},
						serialController.OnConnected.Invoke,
						serialController.OnDisconnected.Invoke,
						(error) => { Debug.LogError(error); });

					serialController.Init(
						serial,
						false);
					connectionUi.SetActive(false);
			});

			// Set up on-connected behavior to report status and set 'connect' button state to allow
			// user to disconnect
			serialController.OnConnected.AddListener(() =>
			{
				status.text =
					string.Format(
						"Connected to {0} at baud: {1}",
						serialController.SerialThread.Port.PortName,
						serialController.SerialThread.Port.BaudRate);
				SetConnectButtonStateConnected();
				sendButton.interactable = true;
			});

			// Set up on-disconnected behavior to report status and set 'connect' button state back
			// to allow user to reconnect
			serialController.OnDisconnected.AddListener(() =>
			{
				status.text = "Disconnected";
				SetConnectButtonStateDisconnected();
				sendButton.interactable = false;
			});

			// And finally, send the input field's text to the serial port.
			sendButton.onClick.AddListener(() =>
			{
				if (!string.IsNullOrEmpty(sendInputField.text))
				{
					serialController.SendSerialMessage(sendInputField.text, false);
					sentTerminal.AddLine(sendInputField.text);
					sendInputField.Select();
					sendInputField.ActivateInputField();
					StartCoroutine(ScrollSentterminal());
				}
			});
		}

		void Update()
		{
			// If user presses ENTER and port is connected then send.
			if (Input.GetKeyDown(KeyCode.Return) && sendButton.interactable)
			{
				sendButton.onClick.Invoke();
			}
		}

		IEnumerator ScrollSentterminal()
		{
			yield return new WaitForEndOfFrame();

			sentTerminal.scrollRect.verticalNormalizedPosition = 0;
		}

		void SetConnectButtonStateConnected()
		{
			connectDisconnectButton.onClick.RemoveAllListeners();
			connectDisconnectButton.onClick.AddListener(() =>
			{
				connectButtonText.text = "Closing...";
				connectDisconnectButton.interactable = false;
				serialController.Close();
			});
			connectButtonText.text = "Disconnect";
		}

		void SetConnectButtonStateDisconnected()
		{
			connectDisconnectButton.interactable = true;
			connectDisconnectButton.onClick.RemoveAllListeners();
			connectDisconnectButton.onClick.AddListener(() =>
			{
				UpdateAvailableComPorts();
				connectionUi.gameObject.SetActive(true);
			});
			connectButtonText.text = "Connect";
		}

		void UpdateAvailableComPorts()
		{
			var availablePorts = SerialPort.GetPortNames();

			comPortDropdown.ClearOptions();

			var options = new List<Dropdown.OptionData>();

			int currentSelectedIndex = -1;

			// Restore last used com
			if (string.IsNullOrEmpty(selectedComPort))
			{
				selectedComPort = PlayerPrefs.GetString("COM", "");
			}

			for (int i = 0; i < availablePorts.Length; i++)
			{
				if (availablePorts[i] == selectedComPort)
				{
					currentSelectedIndex = i;
				}

				options.Add(new Dropdown.OptionData(availablePorts[i]));
			}

			comPortDropdown.AddOptions(options);

			if (currentSelectedIndex > -1)
			{
				comPortDropdown.value = currentSelectedIndex;
			}
		}
	}
}