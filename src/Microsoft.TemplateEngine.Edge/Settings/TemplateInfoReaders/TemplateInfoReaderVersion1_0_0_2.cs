// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Microsoft.TemplateEngine.Edge.Settings.TemplateInfoReaders
{
    internal class TemplateInfoReaderVersion1_0_0_2 : TemplateInfoReaderVersion1_0_0_1
    {
        internal static new TemplateInfo FromJObject(JObject jObject)
        {
            TemplateInfo info = new TemplateInfo();

            TemplateInfoReaderVersion1_0_0_2 reader = new TemplateInfoReaderVersion1_0_0_2();
            return reader.Read(jObject);
        }

        protected override void ReadShortNameInfo(JObject jObject, TemplateInfo info)
        {
            JToken shortNameToken = jObject.Get<JToken>(nameof(TemplateInfo.ShortNameList));
            info.ShortNameList = JTokenStringOrArrayToCollection(shortNameToken, System.Array.Empty<string>());

            if (info.ShortNameList.Count == 0)
            {
                // template.json stores the short name(s) in ShortName
                // but the cache will store it in ShortNameList
                base.ReadShortNameInfo(jObject, info);
            }
        }

        private static IReadOnlyList<string> JTokenStringOrArrayToCollection(JToken token, string[] defaultSet)
        {
            if (token == null)
            {
                return defaultSet;
            }

            if (token.Type == JTokenType.String)
            {
                string tokenValue = token.ToString();
                return new List<string>() { tokenValue };
            }

            return token.ArrayAsStrings();
        }
    }
}
