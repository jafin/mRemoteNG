using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using mRemoteNG.App;

// ReSharper disable ArrangeAccessorOwnerBody

namespace mRemoteNG.Tools.Cmdline;

[SupportedOSPlatform("windows")]
//
//* Arguments class: application arguments interpreter
//*
//* Authors:		R. LOPES
//* Contributors:	R. LOPES
//* Created:		25 October 2002
//* Modified:		28 October 2002
//*
//* Version:		1.0
//
public class CmdArgumentsInterpreter
{
    /// <summary>
    /// What a switch given no value is worth. Callers that expect a value can compare against it to
    /// tell "the switch was not given one" from "the switch was given this".
    /// </summary>
    public const string FlagValue = "true";

    /// <summary>Strips one enclosing quote from each end, as this class has always done.</summary>
    private static readonly Regex QuoteRemover =
        new("^[\'\"]?(.*?)[\'\"]?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly StringDictionary _parameters;

    // Retrieve a parameter value if it exists
    public string? this[string param] => _parameters[param];

    public CmdArgumentsInterpreter(IEnumerable<string> args)
    {
        _parameters = [];
        string? pending = null;

        // Valid parameters forms:
        // {-,/,--}param{ ,=,:}((",')value(",'))
        // Examples: -param1 value1 --param2 /param3:"Test-:-work" /param4=happy -param5 '--=nice=--'

        try
        {
            foreach (string txt in args)
            {
                if (CommandLineSwitch.TryParse(txt, out string name, out string? inlineValue))
                {
                    // A switch ends whatever was waiting: it was given no value, so it is a flag.
                    Remember(pending, FlagValue);
                    pending = null;

                    if (inlineValue == null)
                        pending = name;
                    else
                        Remember(name, Unquote(inlineValue));

                    continue;
                }

                if (pending != null)
                {
                    // Whole, and unexamined. Whatever the value contains is the value.
                    Remember(pending, Unquote(txt));
                    pending = null;
                    continue;
                }

                // A value with no switch waiting for it. Discarded rather than promoted to a
                // parameter of its own: an invented switch nobody defined cannot be told apart from
                // a typo, so the mistake produced neither an error nor an effect.
            }

            // A switch at the end of the line, still waiting. It is a flag.
            Remember(pending, FlagValue);
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector.AddExceptionMessage("Creating new Args failed", ex);
        }
    }

    /// <summary>
    /// First occurrence wins, which is what the original loop did by testing before every add.
    /// </summary>
    private void Remember(string? parameter, string value)
    {
        if (parameter == null || _parameters.ContainsKey(parameter))
            return;

        _parameters.Add(parameter, value);
    }

    private static string Unquote(string value) => QuoteRemover.Replace(value, "$1");
}