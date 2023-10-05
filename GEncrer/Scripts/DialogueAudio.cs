using Godot;
namespace EncrerAudio{
	public partial class DialogueAudio : Resource{
		public string id;
		[Export]
		public AudioStream[] dialogueTypingSoundClips;
		[Export]
		public int frequencyLevel = 2;
		[Export]
		public float minPitch = 0.5f;
		[Export]
		public float maxPitch = 3f;
		public bool stopAudioSource;
	}
}
