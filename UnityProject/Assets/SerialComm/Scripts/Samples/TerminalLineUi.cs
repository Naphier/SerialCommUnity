using UnityEngine;
using UnityEngine.UI;

[System.Serializable]
public class TerminalLineUi
{
	[SerializeField]
	Text linePrefab;

	public ScrollRect scrollRect;

	public void AddLine(string line)
	{
		var newLine = GameObject.Instantiate(linePrefab, linePrefab.transform.parent);
		newLine.transform.localPosition = linePrefab.transform.localPosition;
		newLine.transform.localScale = linePrefab.transform.localScale;
		newLine.gameObject.SetActive(true);
		newLine.text = line;
	}
}
