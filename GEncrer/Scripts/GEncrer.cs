using Godot;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Ink.Runtime;
using EncrerAudio;

public partial class InkExternalFunctions : Node{
	public void Bind(Story story)
	{
		story.BindExternalFunction("background", (string backgroundname) => ChangeBackground(backgroundname));
		story.BindExternalFunction("portraitstate", (bool state) => ShowPortrait(state));
	}

	public void Unbind(Story story) 
	{
		story.UnbindExternalFunction("playEmote");
		story.UnbindExternalFunction("background");
	}

	public void ChangeBackground(string imageName)
	{
		GEncrer d = GEncrer.GetInstance();
		Node2D targetObject = d.background;

		if (targetObject == null)
		{
			GD.Print("Target object not assigned.");
			return;
		}
		TextureRect sprite = targetObject.GetNode<TextureRect>("Sprite");
		Texture[] textures = d.backgroundTextures;
		foreach (Texture texture in textures){
			string textureName = GD.Load<Texture>(texture.ResourcePath).ResourceName;
			if (textureName == imageName){
				sprite.Texture = (Texture2D)texture;
				break;
			}
		}
	}
	public void ShowPortrait(bool state)
	{
		GEncrer d = GEncrer.GetInstance();
		TextureRect portrait = d.portrait.GetNode<TextureRect>("Sprite");
		portrait.Visible = state;
	}

	// ... Rest of your code ...
}
public partial class DialogueVariables : Node{
	public Dictionary<string, Ink.Runtime.Object> Variables { get; private set; }
	private Story globalVariablesStory;
	private const string SaveVariablesKey = "INK_VARIABLES";

	public DialogueVariables(string loadGlobalsJSON)
	{
		// Create the story
		globalVariablesStory = new Story(loadGlobalsJSON);
		// Initialize the dictionary
		Variables = new Dictionary<string, Ink.Runtime.Object>();
		foreach (string name in globalVariablesStory.variablesState)
		{
			Ink.Runtime.Object value = globalVariablesStory.variablesState.GetVariableWithName(name);
			Variables.Add(name, value);
			GD.Print("Initialized global dialogue variable: " + name + " = " + value);
		}
	}

	public void SaveVariables()
	{
		if (globalVariablesStory != null)
		{
			VariablesToStory(globalVariablesStory);
		}
	}

	public void StartListening(Story story)
	{
		// It's important that VariablesToStory is before assigning the listener!
		VariablesToStory(story);
		story.variablesState.variableChangedEvent += VariableChanged;
	}

	public void StopListening(Story story)
	{
		story.variablesState.variableChangedEvent -= VariableChanged;
	}

	private void VariableChanged(string name, Ink.Runtime.Object value)
	{
		// Only maintain variables that were initialized from the globals ink file
		if (Variables.ContainsKey(name))
		{
			Variables.Remove(name);
			Variables.Add(name, value);
		}
	}

	private void VariablesToStory(Story story)
	{
		foreach (KeyValuePair<string, Ink.Runtime.Object> variable in Variables)
		{
			story.variablesState[variable.Key] = variable.Value;
		}
	}
}
public partial class GEncrer : Node{
	[Export]
	public float typingSpeed = 1f;

	[Export]
	public string globalspath;
	public string globals;
	[Export]
	public string inkpath;
	public string inkJSON;
	
	[Export]
	public Node2D fileloader;
	
	[Export]
	public Node2D dialoguePanel;
	[Export]
	public Sprite2D continueIcon;
	[Export]
	public RichTextLabel dialogueText;
	[Export]
	public RichTextLabel displayNameText;
	[Export]
	public Node2D[] choices;
	[Export]
	public Label[] choicesText;
	[Export]
	public Sprite2D background;
	[Export]
	public Texture[] backgroundTextures;
	[Export]
	public Node2D portrait;
	[Export]
	public Texture[] portraitTextures;
	
	public AnimationPlayer portraitAnimator;
	public AnimationPlayer layoutAnimator;
	public AnimationPlayer emoteAnimator;
	
	[Export]
	public DialogueAudio defaultAudioInfo;
	[Export]
	public DialogueAudio[] audioInfos;
	public bool makePredictable = true;
	[Export]
	public DialogueAudio currentAudioInfo;
	[Export]
	public AudioStreamPlayer audioSource;
	
	private Ink.Runtime.Story currentStory;
	private bool dialogueIsPlaying = false;

	private bool canContinueToNextLine = false;
	private static GEncrer instance;

	//Tags
	private const string SPEAKER_TAG = "speaker";
	private const string PORTRAIT_TAG = "portrait";
	private const string AUDIO_TAG = "audio";
	private const string FONT_TAG = "font";

	//Variables and External Functions
	private DialogueVariables dialogueVariables;
	private InkExternalFunctions inkExternalFunctions;
	
	//Coroutines
	private Dictionary<Action, bool> coroutineRunning = new Dictionary<Action, bool>();
	private async void StartCoroutine(Action action){
		if (coroutineRunning.ContainsKey(action) && coroutineRunning[action])
			return;
		coroutineRunning[action] = true;
		while (coroutineRunning[action])
		{
			action.Invoke();
			await ToSignal(GetTree().CreateTimer(1.0f), "timeout");
		}
	}
	private void StopCoroutine(Action action){
		if (coroutineRunning.ContainsKey(action) && coroutineRunning[action]){
			coroutineRunning[action] = false;
		}
	}
	private void _OnTimeout(){ 
		foreach (var action in coroutineRunning.Keys){
			StopCoroutine(action);
		}
	}
	
	private string fileload(string filePath){
		string absolutePath = ProjectSettings.GlobalizePath(filePath);
		string fileText = "";
		if (System.IO.File.Exists(absolutePath)){
			try{
				fileText = System.IO.File.ReadAllText(absolutePath);
				GD.Print("File Content: " + absolutePath);
			}
			catch{
				GD.Print("File not found: " + absolutePath);
			}
		}
		else{
			GD.Print("File not found: Ext: " + absolutePath);
		}
		return fileText;
	}
	//OnReady
	public override void _Ready(){
		if (instance != null)
		{
			GD.Print("Found more than one Dialogue Manager in the scene");
		}
		instance = this;
		globals = fileload(globalspath);
		inkJSON = fileload(inkpath);
		if(globals == "" || inkJSON == ""){
			GD.Print("Did Not Load Required Files");
		}
		dialogueVariables = new DialogueVariables(inkJSON);
		inkExternalFunctions = new InkExternalFunctions();
		audioSource = new AudioStreamPlayer();
		AddChild(audioSource);
		currentAudioInfo = defaultAudioInfo as DialogueAudio;
		
		dialogueIsPlaying = true;
		dialoguePanel.Visible = true;
		int index = 0;
		if (choices.Length != choicesText.Length){
			GD.PrintErr("Error: The 'choices' and 'choicesText' arrays must have the same length.");
			return;
		}
		EnterDialogueMode(inkJSON);
	}

	public static GEncrer GetInstance(){
		return instance;
	}

	public override void _Process(double delta){
		// return right away if dialogue isn't playing
		if (!dialogueIsPlaying)
		{
			return;
		}

		// handle continuing to the next line in the dialogue when submit is pressed
		// NOTE: The 'currentStory.currentChoices.Count == 0' part was to fix a bug after the Youtube video was made
		if (canContinueToNextLine
			&& currentStory.currentChoices.Count == 0
			&& Input.IsActionJustPressed("ui_accept"))
		{
			ContinueStory();
		}
	}

	public void EnterDialogueMode(string inkJSON){
		currentStory = new Ink.Runtime.Story(inkJSON);
		dialogueIsPlaying = true;
		dialoguePanel.Visible = true;
		dialogueVariables.StartListening(currentStory);
		inkExternalFunctions.Bind(currentStory);

		// reset portrait, layout, and speaker
		inkExternalFunctions.ShowPortrait(false);
		displayNameText.Text = "???";
		ContinueStory();
	}

	private async Task ExitDialogueMode()
	{
		await ToSignal(GetTree().CreateTimer(0.2f), "timeout");

		dialogueVariables.StopListening(currentStory);
		inkExternalFunctions.Unbind(currentStory);

		dialogueIsPlaying = false;
		dialoguePanel.Visible = false;
		dialogueText.Text = "";

		// Couldnt properly port this
		//SetCurrentAudioInfo(defaultAudioInfo);
	}
	private void ContinueStory(){
		if (currentStory.canContinue)
		{
			string nextLine = currentStory.Continue();
			// handle case where the last line is an external function
			if (nextLine.Equals("") && !currentStory.canContinue)
			{
				GD.Print("Exiting dialogue mode");
				StartCoroutine(() => ExitDialogueMode());
			}
			// otherwise, handle the normal case for continuing the story
			else
			{
				// handle tags
				HandleTags(currentStory.currentTags);
				StartCoroutine(() => DisplayLine(nextLine));
			}
		}
		else
		{
			StartCoroutine(() => ExitDialogueMode());
		}
	}

	private IEnumerator DisplayLine(string line)
	{
		// set the text to the full line, but set the visible characters to 0
		dialogueText.Text = line;
		dialogueText.VisibleCharacters = 0;
		// hide items while text is typing
		continueIcon.Visible = false;
		HideChoices();

		canContinueToNextLine = false;

		bool isAddingRichTextTag = false;

		// display each letter one at a time
		foreach (char letter in line.ToCharArray())
		{
			// if the submit button is pressed, finish up displaying the line right away
			if (Input.IsActionJustPressed("ui_accept"))
			{
				dialogueText.VisibleCharacters = line.Length;
				break;
			}

			// check for rich text tag, if found, add it without waiting
			if (letter == '<' || isAddingRichTextTag)
			{
				isAddingRichTextTag = true;
				if (letter == '>')
				{
					isAddingRichTextTag = false;
				}
			}
			// if not rich text, add the next letter and wait a small time
			else
			{
				PlayDialogueSound(dialogueText.VisibleCharacters, letter);
				dialogueText.VisibleCharacters++;
				yield return (float)typingSpeed;
			}
		}

		// actions to take after the entire line has finished displaying
		continueIcon.Visible = true;
		DisplayChoices();

		canContinueToNextLine = true;
	}

	private void PlayDialogueSound(int currentDisplayedCharacterCount, char currentCharacter)
	{
		// set variables for the below based on our config
		AudioStream[] dialogueTypingSoundClips = currentAudioInfo.dialogueTypingSoundClips as AudioStream[];
		int frequencyLevel = (int)currentAudioInfo.frequencyLevel;
		float minPitch = (float)currentAudioInfo.minPitch;
		float maxPitch = (float)currentAudioInfo.maxPitch;
		bool stopAudioSource = (bool)currentAudioInfo.stopAudioSource;

		// play the sound based on the config
		if (currentDisplayedCharacterCount % frequencyLevel == 0)
		{
			if (stopAudioSource)
			{
				audioSource.Stop();
			}
			AudioStream soundClip = null;
			// create predictable audio from hashing
			if (makePredictable)
			{
				int hashCode = currentCharacter.GetHashCode();
				// sound clip
				int predictableIndex = hashCode % dialogueTypingSoundClips.Length;
				soundClip = dialogueTypingSoundClips[predictableIndex];
				// pitch
				int minPitchInt = (int)(minPitch * 100);
				int maxPitchInt = (int)(maxPitch * 100);
				int pitchRangeInt = maxPitchInt - minPitchInt;
				// cannot divide by 0, so if there is no range then skip the selection
				if (pitchRangeInt != 0)
				{
					int predictablePitchInt = (hashCode % pitchRangeInt) + minPitchInt;
					float predictablePitch = predictablePitchInt / 100f;
					audioSource.PitchScale = predictablePitch;
				}
				else
				{
					audioSource.PitchScale = minPitch;
				}
			}
			// otherwise, randomize the audio
			else
			{
				// sound clip
				int randomIndex = (int)GD.RandRange(0, dialogueTypingSoundClips.Length);
				soundClip = dialogueTypingSoundClips[randomIndex];
				// pitch
				audioSource.PitchScale = (float)GD.RandRange(minPitch, maxPitch);
			}

			// play sound
			audioSource.Stream = soundClip;
			audioSource.Play();
		}
	}

	private void HideChoices()
	{
		foreach (Node2D choiceButton in choices)
		{
			choiceButton.Visible = false;
		}
	}

	private void HandleTags(List<string> currentTags)
	{
		// loop through each tag and handle it accordingly
		foreach (string tag in currentTags)
		{
			// parse the tag
			string[] splitTag = tag.Split(':');
			if (splitTag.Length != 2) { GD.Print("Tag could not be appropriately parsed: " + tag); }
			string tagKey = splitTag[0].Trim();
			string tagValue = splitTag[1].Trim();

			// handle the tag
			switch (tagKey)
			{
				case SPEAKER_TAG:
					displayNameText.Text = tagValue;
					break;
				case PORTRAIT_TAG:
					inkExternalFunctions.ShowPortrait(true);
					TextureRect portraitImage = portrait.GetNode<TextureRect>("Image");
					Texture[] textures = portraitTextures;
					bool textureFound = false;
					foreach (Texture texture in textures)
					{
						GD.Print("Loaded Texture: " + texture.ResourceName);
						if (texture.ResourceName == tagValue)
						{
							portraitImage.Texture = (Texture2D)texture;
							textureFound = true;
							break;
						}
					}
					if (!textureFound)
					{
						GD.Print("Did not Find Texture with name: " + tagValue);
					}
					break;
				case AUDIO_TAG:
					GD.Print("This System Currently Doesnt Work, My Apologies");
					break;
				case FONT_TAG:
					GD.Print("This System Currently Doesnt Work, My Apologies");
					break;
				default:
					GD.Print("Tag came in but is not currently being handled: " + tag);
					break;
			}
		}
	}

	private void DisplayChoices()
	{
		List<Ink.Runtime.Choice> currentChoices = currentStory.currentChoices;

		// defensive check to make sure our UI can support the number of choices coming in
		if (currentChoices.Count > choices.Length)
		{
			GD.Print("More choices were given than the UI can support. Number of choices given: "
				+ currentChoices.Count);
		}

		int index = 0;
		// enable and initialize the choices up to the amount of choices for this line of dialogue
		foreach (Ink.Runtime.Choice choice in currentChoices)
		{
			choices[index].Visible = true;
			choicesText[index].Text = choice.text;
			index++;
		}
		// go through the remaining choices the UI supports and make sure they're hidden
		for (int i = index; i < choices.Length; i++)
		{
			choices[i].Visible = false;
		}

		StartCoroutine(() => SelectFirstChoice());
	}
	private IEnumerator SelectFirstChoice()
	{
		yield return null;
	}
	public void MakeChoice(int choiceIndex)
	{
		if (canContinueToNextLine)
		{
			currentStory.ChooseChoiceIndex(choiceIndex);
			Input.ActionPress("ui_accept");
			ContinueStory();
		}
	}

	public Ink.Runtime.Object GetVariableState(string variableName)
	{
		Ink.Runtime.Object variableValue = null;
		dialogueVariables.Variables.TryGetValue(variableName, out variableValue);
		if (variableValue == null)
		{
			GD.Print("Ink Variable was found to be null: " + variableName);
		}
		return variableValue;
	}

	// This method will get called anytime the application exits.
	public void OnMainLoopQuit()
	{
		dialogueVariables.SaveVariables();
	}
}
