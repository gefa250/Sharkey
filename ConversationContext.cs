using System;
using System.Collections.Generic;
using System.Linq;

namespace GlobalTranslator
{
    internal sealed class ConversationContext
    {
        internal CommunicationTurn[] Turns;
        internal byte[][] Images;
        internal string ImageContext;
        internal bool LimitedTurns;
        internal bool LimitedImages;

        internal static ConversationContext Create(IEnumerable<CommunicationTurn> history, int start, byte[][] draft)
        {
            var active = history.Skip(Math.Max(0, start)).ToArray();
            var turns = active.Skip(Math.Max(0, active.Length - 10)).ToArray();
            var pictures = new List<Tuple<byte[], string>>();
            for (int i = 0; i < turns.Length; i++)
                foreach (byte[] bytes in turns[i].Images ?? new byte[0][])
                    pictures.Add(Tuple.Create(bytes, "Previous message " + (i + 1) + ": " + turns[i].Instruction));
            foreach (byte[] bytes in draft ?? new byte[0][])
                pictures.Add(Tuple.Create(bytes, "Current user message"));
            var selected = pictures.Skip(Math.Max(0, pictures.Count - 5)).ToArray();
            return new ConversationContext
            {
                Turns = turns.Select(t => new CommunicationTurn { Instruction = t.Instruction, Reply = t.Reply }).ToArray(),
                Images = selected.Select(p => p.Item1).ToArray(),
                ImageContext = string.Join("\n", selected.Select((p, i) => "Image " + (i + 1) + " — " + p.Item2)),
                LimitedTurns = active.Length > 10,
                LimitedImages = pictures.Count > 5
            };
        }
    }
}
