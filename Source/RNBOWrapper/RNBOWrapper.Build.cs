// Copyright Epic Games, Inc. All Rights Reserved.

using UnrealBuildTool;
using EpicGames.Core;
using System;
using System.IO;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;


/*
//Copied from unity project, TODO, Consolidate
[System.Serializable]
public class ParameterInfo {
	public int index;

	public string name;
	public string paramId;
	public string displayName;
	public string unit;

	public Float minimum;
	public Float maximum;
	public Float initialValue;
	public int steps;

	public List<string> enumValues;
	public string meta;
}

[System.Serializable]
public class Port {
	public string tag;
	public string meta;
}

[System.Serializable]
public class DataRef {
	public string id;
	public string type;
	public string file;
}

[System.Serializable]
public class PatcherDescription {
	public int numParameters;
	public List<ParameterInfo> parameters;
	public List<Port> inports;
	public List<Port> outports;
	public List<DataRef> externalDataRefs;
}
*/

public class RNBOWrapper : ModuleRules
{
	string OperatorTemplate { get; set; }

	public RNBOWrapper(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = ModuleRules.PCHUsageMode.UseExplicitOrSharedPCHs;

		var templateFile = Path.Combine(PluginDirectory, "Source", "RNBOWrapper", "Template", "MetaSoundOperator.cpp.in");
		using (StreamReader streamReader = new StreamReader(templateFile, Encoding.UTF8))
		{
			OperatorTemplate = streamReader.ReadToEnd();
		}

		var exportDir = Path.Combine(PluginDirectory, "Exports");
		string rnboDir = null;

		using (StreamWriter writer = new StreamWriter(Path.Combine(PluginDirectory, "Source", "RNBOWrapper", "Private", "RNBOWrapperGenerated.cpp")))
		{
			writer.WriteLine("//automatically generated by RNBOWrapper");
			writer.WriteLine("#include \"RNBO.cpp\" ");

			//detect exports, add includes and generate metasounds
			foreach (var path in Directory.GetDirectories(exportDir)) {
				//include export dir
				PrivateIncludePaths.Add(path);
				//set rnbo dir
				if (rnboDir == null) {
					rnboDir = Path.Combine(path, "rnbo");
				}

				//#include cpp files in export dir
				foreach (var f in Directory.GetFiles(path, "*.cpp")) {
					writer.WriteLine("#include \"{0}/{1}\" ", new DirectoryInfo(path).Name, Path.GetFileName(f));
				}
				writer.Write(CreateMetaSound(path));
			}
		}

		ExternalDependencies.Add(templateFile);
		
		PublicIncludePaths.AddRange(
			new string[] {
				// ... add public include paths required here ...
			}
			);
				

		PrivateIncludePaths.AddRange(
			new string[]
			{
				exportDir,
				Path.Combine(rnboDir),
				Path.Combine(rnboDir, "common"),
				Path.Combine(rnboDir, "src"),
				Path.Combine(rnboDir, "src", "3rdparty"),
			}
			);
		

		PublicDependencyModuleNames.AddRange(
			new string[]
			{
				"Core",
				"MetasoundFrontend",
				"MetasoundGraphCore",
				"MetasoundStandardNodes",
				// ... add other public dependencies that you statically link with here ...
			}
			);
			
		
		PrivateDependencyModuleNames.AddRange(
			new string[]
			{
				"CoreUObject",
				"Engine",
				"SignalProcessing",
				//Path.Combine(rnboDir, "RNBO.cpp"),
				// ... add private dependencies that you statically link with here ...	
			}
			);
		
		
		DynamicallyLoadedModuleNames.AddRange(
			new string[]
			{
				// ... add any modules that your module loads dynamically here ...
			}
			);

		PrivateDefinitions.Add("RNBO_NO_PATCHERFACTORY=1");
	}

	string CreateMetaSound(string path) {
		Regex rx = new Regex(@"PatcherFactoryFunctionPtr\s*(?<name>\w+)FactoryFunction", RegexOptions.Compiled);

		var descPath = Path.Combine(path, "description.json");
		JsonObject desc = JsonObject.Read(new FileReference(descPath));

		//get name from cpp file
		//TODO get some of this from metadata?
		string name = null;

		foreach (var filePath in Directory.GetFiles(path, "*.cpp")) {
			using (var f = File.OpenRead(filePath))
			{
				var s = new StreamReader(f);

				while (!s.EndOfStream)
				{
					var line = s.ReadLine();
					var match = rx.Match(line);
					if (match.Success) {
						string n = match.Groups["name"].Value;
						if (n != "GetPatcher") {
							name = n;
							break;
						}
					}
				}

				f.Close();
			}
		}

		if (name == null) {
			throw new ArgumentException(String.Format("Cannot find class name for export at: {0}", path), "path");
		}

		var ns = name + "Operator";
		var displayName = name;
		var description = "Test MetaSound";
		var category = "Utility";

		var v = OperatorTemplate
		.Replace("_OPERATOR_NAMESPACE_", ns)
		.Replace("_OPERATOR_NAME_", name)
		.Replace("_OPERATOR_DISPLAYNAME_", displayName)
		.Replace("_OPERATOR_DESCRIPTION_", description)
		.Replace("_OPERATOR_CATEGORY", category)
		;

		List<string> paramDecl = new List<string>();
		List<string> memberDecl = new List<string>();
		List<string> memberInit = new List<string>();
		List<string> paramUpdate = new List<string>();
		List<string> vertexInputs = new List<string>();
		List<string> vertexOutputs = new List<string>();
		List<string> getInputs = new List<string>();
		List<string> getOutputs = new List<string>();
		
		foreach (JsonObject param in desc.GetObjectArrayField("parameters")) {
			if (param.GetBoolField("visible")) {
				string t = param.GetStringField("type");

				string n = param.GetStringField("name");
				string id = param.GetStringField("paramId");
				int index = param.GetIntegerField("index");
				double initial = param.GetDoubleField("initialValue");

				//TODO description etc from meta
				string pname = String.Format("InParam{0}", id);
				string mname = String.Format("Param{0}", id);

				paramDecl.Add(String.Format("METASOUND_PARAM({0}, \"{1}\", \"{1}\")", pname, name));

				vertexInputs.Add(String.Format("TInputDataVertex<float>(METASOUND_GET_PARAM_NAME_AND_METADATA({0}), {1}f)", pname, initial.ToString("N", CultureInfo.InvariantCulture)));
				paramUpdate.Add(String.Format("UpdateParam({0}, *{1});", index, mname));

				memberDecl.Add(String.Format("FFloatReadRef {0};", mname));
				memberInit.Add(String.Format("{0}(InputCollection.GetDataReadReferenceOrConstructWithVertexDefault<float>(InputInterface, METASOUND_GET_PARAM_NAME({1}), InSettings))", mname, pname));

				getInputs.Add(String.Format("InputDataReferences.AddDataReadReference(METASOUND_GET_PARAM_NAME({0}), {1});", pname, mname));
			}
		}

		int inputs = desc.GetIntegerField("numInputChannels");
		int outputs = desc.GetIntegerField("numOutputChannels");
		List<string> inputAudioInit = new List<string>();
		List<string> outputAudioInit = new List<string>();

		string numFramesMember = null;
		for (int i = 0; i < inputs; i++)
		{
			string mname = String.Format("AudioInput{0}", i);
			string pname = String.Format("InParamAudio{0}", i);

			numFramesMember = mname;

			memberDecl.Add(String.Format("FAudioBufferReadRef {0};", mname));
			paramDecl.Add(String.Format("METASOUND_PARAM({0}, \"In {1}\", \"In {1}\")", pname, i + 1));

			inputAudioInit.Add(String.Format("{0}->GetData()", mname));
			memberInit.Add(String.Format("{0}(InputCollection.GetDataReadReferenceOrConstruct<FAudioBuffer>(METASOUND_GET_PARAM_NAME({1}), InParams.OperatorSettings)", mname, pname));

			vertexInputs.Add(String.Format("TInputDataVertex<FAudioBuffer>(METASOUND_GET_PARAM_NAME_AND_METADATA({0}))", pname));

			getInputs.Add(String.Format("InputDataReferences.AddDataReadReference(METASOUND_GET_PARAM_NAME({0}), {1});", pname, mname));
		}

		for (int i = 0; i < outputs; i++)
		{
			string mname = String.Format("AudioOutput{0}", i);
			string pname = String.Format("OutParamAudio{0}", i);

			numFramesMember = mname;

			memberDecl.Add(String.Format("FAudioBufferWriteRef {0};", mname));
			paramDecl.Add(String.Format("METASOUND_PARAM({0}, \"Out {1}\", \"Out {1}\")", pname, i + 1));

			outputAudioInit.Add(String.Format("{0}->GetData()", mname));
			memberInit.Add(String.Format("{0}(FAudioBufferWriteRef::CreateNew(InSettings))", mname));

			vertexOutputs.Add(String.Format("TOutputDataVertex<FAudioBuffer>(METASOUND_GET_PARAM_NAME_AND_METADATA({0}))", pname));
			getOutputs.Add(String.Format("OutputDataReferences.AddDataReadReference(METASOUND_GET_PARAM_NAME({0}), {1});", pname, mname));
		}

		v = v
			.Replace("_OPERATOR_VERTEX_INPUTS_", String.Join(", ", vertexInputs))
			.Replace("_OPERATOR_VERTEX_OUTPUTS_", String.Join(", ", vertexOutputs))
			.Replace("_OPERATOR_GET_INPUTS_", String.Join("\n", getInputs))
			.Replace("_OPERATOR_GET_OUTPUTS_", String.Join("\n", getOutputs))

			.Replace("_OPERATOR_AUDIO_INPUT_COUNT_", inputAudioInit.Count.ToString())
			.Replace("_OPERATOR_AUDIO_INPUT_INIT_", String.Join(", ", inputAudioInit))
			.Replace("_OPERATOR_AUDIO_OUTPUT_COUNT_", outputAudioInit.Count.ToString())
			.Replace("_OPERATOR_AUDIO_OUTPUT_INIT_", String.Join(", ", outputAudioInit))
			.Replace("_OPERATOR_MEMBERS_DECL_", String.Join("\n", memberDecl))
			.Replace("_OPERATOR_MEMBERS_INIT_", memberInit.Count == 0 ? " " : ", " + String.Join(",\n", memberInit))
			.Replace("_OPERATOR_PARAM_DECL_", String.Join("\n", paramDecl))
			.Replace("_OPERATOR_PARAM_UPDATE_", String.Join("\n", paramUpdate)) 
			.Replace("_OPERATOR_AUDIO_NUMFRAMES_MEMBER_", numFramesMember)
			;

		return v;
	}
}
