using System;
using System.Collections.Generic;
using UnityEngine;

namespace Runeterra.Core
{
    /// <summary>
    /// Шина команд партии: проверка, запись в журнал, применение. Ходы ИИ тоже идут через неё:
    /// после конца хода человека шина отыгрывает стороны ИИ (их команды попадают в журнал),
    /// пока снова не придёт ход человека. Без ИИ (aiTurn = null) шина только применяет команды —
    /// так журнал проигрывается заново (реплей).
    /// </summary>
    public sealed class CommandBus
    {
        private readonly GameState _game;
        private readonly Action<PlayerState> _aiTurn;
        private bool _runningAi;

        /// <summary>Журнал применённых команд с начала партии (или с загрузки).</summary>
        public List<ICommand> Journal { get; } = new List<ICommand>();

        /// <summary>Команда применена (после изменения состояния).</summary>
        public event Action<ICommand> Executed;

        public CommandBus(GameState game, Action<PlayerState> aiTurn)
        {
            _game = game;
            _aiTurn = aiTurn;
        }

        /// <summary>Причина, по которой команду нельзя применить (null — можно).</summary>
        public string Validate(ICommand c)
        {
            if (c == null) return "нет команды";
            if (c.Player != _game.Turns.CurrentIndex) return "не ваш ход";
            return c.Validate(_game);
        }

        /// <summary>Проверить и применить. false — команда отклонена или ничего не изменила.</summary>
        public bool Execute(ICommand c)
        {
            if (Validate(c) != null) return false;
            Journal.Add(c);
            bool done = c.Apply(_game);
            Executed?.Invoke(c);
            if (c is EndTurnCommand) RunAiTurns();
            return done;
        }

        /// <summary>Отыграть подряд стороны ИИ, пока ход не перейдёт к человеку.</summary>
        public void RunAiTurns()
        {
            if (_aiTurn == null || _runningAi) return;
            _runningAi = true;
            try
            {
                while (!_game.Turns.Current.IsHuman)
                {
                    var p = _game.Turns.Current;
                    _aiTurn(p);
                    Execute(new EndTurnCommand(p.Index));
                }
            }
            finally { _runningAi = false; }
        }

        // ---------- Журнал в JSON ----------

        [Serializable] private class Entry { public string type; public string data; }
        [Serializable] private class EntryList { public List<Entry> commands = new List<Entry>(); }

        /// <summary>Журнал в JSON: тип команды и её поля.</summary>
        public static string ToJson(IEnumerable<ICommand> commands)
        {
            var list = new EntryList();
            foreach (var c in commands) list.commands.Add(new Entry { type = c.GetType().Name, data = JsonUtility.ToJson(c) });
            return JsonUtility.ToJson(list);
        }

        public static List<ICommand> FromJson(string json)
        {
            var result = new List<ICommand>();
            foreach (var e in JsonUtility.FromJson<EntryList>(json).commands)
            {
                var type = typeof(ICommand).Assembly.GetType(typeof(ICommand).Namespace + "." + e.type)
                           ?? throw new FormatException($"неизвестная команда {e.type}");
                var c = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
                JsonUtility.FromJsonOverwrite(e.data, c);
                result.Add((ICommand)c);
            }
            return result;
        }
    }
}
