using System;
using System.Collections.Generic;

namespace Runeterra.Core
{
    /// <summary>
    /// Реплей: журнал команд партии применяется заново к той же стартовой позиции.
    /// Вместо ИИ ходы его сторон берутся из журнала (шина вызывает <see cref="AiTurn"/>),
    /// поэтому порядок применения тот же, что в исходной партии.
    /// </summary>
    public sealed class CommandReplay
    {
        private readonly Queue<ICommand> _queue;
        private GameState _game;

        /// <summary>Первая команда, которую не удалось применить (null — всё сошлось).</summary>
        public string Error { get; private set; }

        public CommandReplay(IEnumerable<ICommand> journal) => _queue = new Queue<ICommand>(journal);

        public void Attach(GameState game) => _game = game;

        /// <summary>Ход стороны ИИ: её команды из журнала до её конца хода (конец хода применяет сама шина).</summary>
        public void AiTurn(PlayerState p)
        {
            while (Error == null && _queue.Count > 0 && !(_queue.Peek() is EndTurnCommand)) Step();
            if (Error != null || _queue.Count == 0) return;
            var end = _queue.Dequeue();
            if (end.Player != p.Index) Error = $"ход {_game.Turns.Turn}: конец хода стороны {end.Player}, а ходит {p.Index}";
        }

        /// <summary>
        /// Применить журнал до конца. afterHumanTurn вызывается в начале хода человека
        /// (до его первой команды и после каждого его конца хода, когда ИИ уже отходили).
        /// </summary>
        public void Run(Action<GameState> afterHumanTurn)
        {
            afterHumanTurn?.Invoke(_game);
            while (Error == null && _queue.Count > 0)
            {
                bool end = _queue.Peek() is EndTurnCommand;
                Step();
                if (end && Error == null) afterHumanTurn?.Invoke(_game);
            }
        }

        private void Step()
        {
            var c = _queue.Dequeue();
            string reason = _game.Commands.Validate(c);
            if (reason != null)
            {
                Error = $"ход {_game.Turns.Turn}: {c.GetType().Name} стороны {c.Player} отклонена — {reason}";
                return;
            }
            _game.Commands.Execute(c);
        }
    }
}
