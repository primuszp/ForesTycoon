using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;
using static ForesTycoon.Editor.RuleCatalogPresentation;
namespace ForesTycoon.Editor;

internal static class RuleCatalogPresentation
{
    internal static Vec4 ExecutionColour(GameRuleExecution e) => e switch
    {
        GameRuleExecution.EditableGraph or GameRuleExecution.EditableController or GameRuleExecution.EditableExpressions => EditorStyle.GraphKind,
        GameRuleExecution.Presentation => EditorStyle.PresentationKind,
        _ => EditorStyle.NativeKind
    };

    internal static string Status(GameRuleExecution execution) => execution switch
    {
        GameRuleExecution.EditableGraph => "Szerkeszthető gráf",
        GameRuleExecution.EditableController => "Szerkeszthető vezérlés",
        GameRuleExecution.EditableExpressions => "Viselkedésképletek",
        GameRuleExecution.Presentation => "Megjelenítés",
        _ => "A motor végzi"
    };

    internal static string StatusHelp(GameRuleExecution execution) => execution switch
    {
        GameRuleExecution.EditableGraph => "A projekt gráfja fut a játékban; a „Megnyitás szerkesztésre” gomb viszi oda.",
        GameRuleExecution.EditableController => "A projekt munkaciklusa vezérli a gépet; a Munkaciklusok oldalon módosítható.",
        GameRuleExecution.EditableExpressions => "A folyamat döntéseit és mennyiségeit a projekt gráfjai adják (Összefüggések, Folyamatkapcsolatok).",
        GameRuleExecution.Presentation => "Csak megjelenítés: a szimuláció állapotát olvassa, nem változtatja.",
        _ => "A játék motorja végzi. A logikája itt nem szerkeszthető; a számai a Játékparaméterek oldalon hangolhatók."
    };

}
