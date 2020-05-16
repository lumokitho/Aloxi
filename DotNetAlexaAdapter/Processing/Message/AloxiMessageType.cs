﻿using System;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ZoolWay.AloxiAlexaAdapter.Processing.Meta
{
    [JsonConverter(typeof(StringEnumConverter))]
    internal enum AloxiMessageType
    {
        [EnumMember(Value = "aloxiComm")]
        AloxiComm,
        [EnumMember(Value = "unknown")]
        Unknown,
    }
}
