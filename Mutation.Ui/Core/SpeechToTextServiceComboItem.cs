using CognitiveSupport;

namespace Mutation.Ui;

internal class SpeechToTextServiceComboItem
{
	public required SpeechToTextServiceSettings SpeechToTextServiceSettings { get; set; }
	public required ISpeechToTextService SpeechToTextService { get; set; }
        public string Display =>
                $"{SpeechToTextServiceSettings.Name}";

	public override string ToString()
	{
		return this.Display;
	}
}
