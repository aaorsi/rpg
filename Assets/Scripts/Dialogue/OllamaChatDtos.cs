using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Rpg.Dialogue
{
    [Serializable]
    public sealed class OllamaMessageDto
    {
        public string role;
        public string content;

        public OllamaMessageDto()
        {
        }

        public OllamaMessageDto(string role, string content)
        {
            this.role = role;
            this.content = content;
        }
    }

    [Serializable]
    public sealed class OllamaChatRequestDto
    {
        public string model;
        public List<OllamaMessageDto> messages;
        public bool stream;
        public OllamaOptionsDto options;
    }

    [Serializable]
    public sealed class OllamaOptionsDto
    {
        [JsonProperty("num_predict")] public int num_predict;
    }
}
