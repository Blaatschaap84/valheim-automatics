using System;
using System.Collections.Generic;
using System.Linq;
using ModUtils;

namespace Automatics.ConsoleCommands
{
    internal class PrintNames : Command
    {
        public PrintNames() : base("printnames")
        {
            HaveExtraOption = true;
            HaveExtraDescription = true;
        }

        protected override void CommandAction(Terminal.ConsoleEventArgs args)
        {
            if (!ParseArgs(args)) return;

            Automatics.Logger.Message(() => $"Command exec: {args.FullLine}");

            var filters = new List<TextFilter>();
            foreach (var arg in extraOptions)
            {
                if (!TryCreateTextFilter(args, arg, out var filter)) return;
                if (filter != null) filters.Add(filter);
            }

            foreach (var (key, value) in from translation in GetAllTranslations()
                     let key = translation.Key.StartsWith("automatics_")
                         ? $"@{translation.Key.Substring(11)}"
                         : $"${translation.Key}"
                     let value = translation.Value
                     where filters.All(filter => filter.IsMatch(key) || filter.IsMatch(value))
                     select (key, value))
            {
                var text =
                    Automatics.L10N.LocalizeTextOnly("@command_printnames_result_format", key,
                        value);
                args.Context.AddString(text);
                Automatics.Logger.Message(() => $"  {text}");
            }

            args.Context.AddString("");

            Dictionary<string, string> GetAllTranslations()
            {
                return Reflections.GetField<Dictionary<string, string>>(Localization.instance,
                    "m_translations");
            }
        }
    }
}
