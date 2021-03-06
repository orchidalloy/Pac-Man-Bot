﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using PacManBot.Constants;
using PacManBot.Extensions;
using PacManBot.Utils;
using Range = PacManBot.Utils.Range;

namespace PacManBot.Games.Concrete
{
    [DataContract]
    public class RubiksGame : ChannelGame, IUserGame, IStoreableGame
    {
        // Constants

        public override int GameIndex => 20;
        public override string GameName => "Rubik's Cube";
        public override TimeSpan Expiry => TimeSpan.FromHours(48);
        public string FilenameKey => "rubik";


        /// <summary>The raw cube string representing the solved state.</summary>
        public const string SolvedCube = "000000000111111111222222222333333333444444444555555555";

        /// <summary>The keys of all valid built-in moves that can be used to create individual moves.</summary>
        public static IEnumerable<string> MoveKeys => AllMoves.Select(x => x.Key);


        private const int // Starting index of each face in the cube array
            Front = 0 * 9,
            Up    = 1 * 9,
            Right = 2 * 9,
            Left  = 3 * 9,
            Down  = 4 * 9,
            Back  = 5 * 9;

        private static readonly IEnumerable<RawMove> AllMoves = CreateMoves();

        private static readonly string[] ColorEmoji = {
            CustomEmoji.GreenSquare, CustomEmoji.WhiteSquare, CustomEmoji.RedSquare,
            CustomEmoji.OrangeSquare, CustomEmoji.YellowSquare, CustomEmoji.BlueSquare,
        };


        

        /// <summary>An array of all stickers of the cube, grouped by face.</summary>
        public Sticker[] cube;

        [DataMember] public bool ShowHelp { get; set; } = true;

        /// <summary>The cube array in raw string form, to be stored and loaded.</summary>
        [DataMember] public string RawCube
        {
            get => cube.Select(x => (int)x).JoinString();
            set => cube = value.Select(x => (Sticker)int.Parse(x.ToString())).ToArray();
        }

        [DataMember] public override int Time { get; set; }
        [DataMember] public override DateTime LastPlayed { get; set; }
        [DataMember] public override ulong OwnerId { get => base.OwnerId; protected set => base.OwnerId = value; }
        [DataMember] public override ulong ChannelId { get => base.ChannelId; set => base.ChannelId = value; }
        [DataMember] public override ulong MessageId { get => base.MessageId; set => base.MessageId = value; }




        // Types

        /// <summary>A sticker color on the cube.</summary>
        public enum Sticker
        {
            Green,
            White,
            Red,
            Orange,
            Yellow,
            Blue,
        }


        /// <summary>An object that represents a transformation to perform on the cube,
        /// based on cycles of sticker indices.</summary>
        private class RawMove : IEnumerable<LoopedList<int>>
        {
            /// <summary>The string that identifies the move.</summary>
            public string Key { get; }

            /// <summary>All sticker cycles of this move, where each sticker needs to be replaced
            /// by the sticker that is next in the cycle.</summary>
            public IEnumerable<LoopedList<int>> Cycles { get; }


            private RawMove() { }

            public RawMove(string key, params int[][] cycles)
            {
                Key = key.ToUpperInvariant();

                var safeCycles = new List<LoopedList<int>>(cycles.GetLength(0));
                foreach (var cycle in cycles)
                {
                    if (cycle.Distinct().Count() != cycle.Length
                        || cycle.Any(x => safeCycles.Any(y => y.Contains(x))))
                    {
                        throw new InvalidOperationException("Cycle values must be unique");
                    }

                    safeCycles.Add(cycle.ToList());
                }

                Cycles = safeCycles;
            }

            public IEnumerator<LoopedList<int>> GetEnumerator() => Cycles.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }


        /// <summary>A move that can be applied to the cube.</summary>
        public class Move
        {
            private RawMove baseMove;
            private int repeat;
            private bool reverse;

            private Move() { }

            /// <summary>Creates a new move from a key in <see cref="MoveKeys"/>.</summary>
            public Move(string key, int repeat = 1, bool reverse = false)
            {
                this.repeat = repeat;
                this.reverse = reverse;
                key = key.ToUpperInvariant();
                baseMove = AllMoves.FirstOrDefault(x => x.Key == key);
                if (baseMove is null) throw new ArgumentException("Key doesn't match any existing built-in move.", nameof(key));
            }


            /// <summary>Applies this move's transformation on the provided cube.</summary>
            public void Apply(Sticker[] cube)
            {
                var oldCube = cube.ToArray();

                foreach (var cycle in baseMove)
                {
                    for (int i = 0; i < cycle.Count; i++)
                    {
                        int index = cycle[i];
                        int oldIndex = cycle[i + (reverse ? +repeat : -repeat)];
                        cube[index] = oldCube[oldIndex];
                    }
                }
            }



            /// <summary>Attempts to create a move from the provided string. The string must follow standard cube notation.</summary>
            public static bool TryParse(string value, out Move move)
            {
                move = null;

                var match = Regex.Match(value, @"^([a-zA-Z]+)([0-9]{0,3})('{0,10})$");
                if (!match.Success) return false;

                var baseMove = AllMoves.FirstOrDefault(x => x.Key == match.Groups[1].Value.ToUpperInvariant());
                if (baseMove is null) return false;

                move = new Move
                {
                    baseMove = baseMove,
                    repeat = match.Groups[2].Length == 0 ? 1 : int.Parse(match.Groups[2].Value),
                    reverse = match.Groups[3].Length % 2 != 0
                };

                return true;
            }
        }
        



        private RubiksGame() { }

        public RubiksGame(ulong channelId, ulong ownerId, IServiceProvider services)
            : base(channelId, new[] { ownerId }, services)
        {
            RawCube = SolvedCube;
        }

        
        /// <summary>Attempts to create a sequence of moves from the given string, and executes it if successful.</summary>
        public bool TryDoMoves(string input)
        {
            var sequence = new List<Move>();

            foreach (string splice in input.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (Move.TryParse(splice, out var move)) sequence.Add(move);
                else return false;
            }

            DoMoves(sequence);
            return true;
        }


        /// <summary>Executes a sequence of moves on this Rubik's cube.</summary>
        public void DoMoves(IEnumerable<Move> moves)
        {
            var copy = cube.ToArray();
            foreach (var move in moves) move.Apply(copy);
            cube = copy; // Replaces the cube all at once for thread-safety
        }


        /// <summary>Executes a number of random moves on the cube in order to scramble it.</summary>
        public void Scramble(int amount = 40)
        {
            var turns = new string[] { "F", "U", "R", "L", "D", "B", };
            var moves = new Range(amount).Select(x => new Move(Program.Random.Choose(turns), Program.Random.Next(1, 4)));

            Time = -amount; // Done before so it saves the game at 0
            DoMoves(moves);
        }

        
        public override async ValueTask<DiscordEmbedBuilder> GetEmbedAsync(bool _ = true)
        {
            var description = new StringBuilder();

            string[] rowsFront = GetFaceRows(Front);
            string[] rowsUp    = GetFaceRows(Up);
            string[] rowsRight = GetFaceRows(Right);
            string[] rowsLeft  = GetFaceRows(Left);
            string[] rowsDown  = GetFaceRows(Down);
            string[] rowsBack  = GetFaceRows(Back);
            string emptyRow = CustomEmoji.Empty.Repeat(3);

            for (int i = 0; i < 3; i++)
            {
                description.Append($"{emptyRow} {rowsUp[i]}\n");
            }

            for (int i = 0; i < 3; i++)
            {
                description.Append($"{rowsLeft[i]} {rowsFront[i]} {rowsRight[i]} {rowsBack[i]}\n");
            }

            for (int i = 0; i < 3; i++)
            {
                description.Append($"{emptyRow} {rowsDown[i]}\n");
            }


            var embed = new DiscordEmbedBuilder
            {
                Title = $"{(await GetOwnerAsync())?.DisplayName()}'s Rubik's Cube",
                Description = description.ToString().Truncate(2048),
                Color = Colors.Black,
            };

            if (ShowHelp)
            {
                string prefix = Storage.GetPrefix(Channel);
                embed.AddField("Faces", $"```css\n  U\nL F R B\n  D```Do **{prefix}rubik moves** for help controlling the cube.");
            }

            return embed;
        }



        private string[] GetFaceRows(int faceIndex)
        {
            var emojis = new Range(faceIndex, faceIndex + 9).Select(x => ColorEmoji[(int)cube[x]]).ToList();

            string[] rows = emojis.Split(3).Select(x => string.Join("", x)).ToArray();
            return rows;
        }


        public void PostDeserialize(IServiceProvider services)
        {
            SetServices(services);
        }




        private static IEnumerable<RawMove> CreateMoves()
        {
            var cyclesF = new[] {
                new[] { Front+0, Front+2, Front+8, Front+6 },
                new[] { Front+1, Front+5, Front+7, Front+3 },
                new[] { Down+2, Left+8, Up+6, Right+0 },
                new[] { Down+1, Left+5, Up+7, Right+3 },
                new[] { Down+0, Left+2, Up+8, Right+6 },
            };

            var cyclesB = new[] {
                new[] { Back+0, Back+2, Back+8, Back+6 },
                new[] { Back+1, Back+5, Back+7, Back+3 },
                new[] { Down+8, Right+2, Up+0, Left+6 },
                new[] { Down+7, Right+5, Up+1, Left+3 },
                new[] { Down+6, Right+8, Up+2, Left+0 },
            };

            var cyclesS = new[] {
                new[] { Down+5, Left+7, Up+3, Right+1 },
                new[] { Down+4, Left+4, Up+4, Right+4 },
                new[] { Down+3, Left+1, Up+5, Right+7 },
            };

            var cyclesU = new[] {
                new[] { Up+0, Up+2, Up+8, Up+6 },
                new[] { Up+1, Up+5, Up+7, Up+3 },
                new[] { Front+0, Left+0, Back+0, Right+0 },
                new[] { Front+1, Left+1, Back+1, Right+1 },
                new[] { Front+2, Left+2, Back+2, Right+2 },
            };

            var cyclesD = new[] {
                new[] { Down+0, Down+2, Down+8, Down+6 },
                new[] { Down+1, Down+5, Down+7, Down+3 },
                new[] { Front+6, Right+6, Back+6, Left+6 },
                new[] { Front+7, Right+7, Back+7, Left+7 },
                new[] { Front+8, Right+8, Back+8, Left+8 },
            };

            var cyclesE = new[] {
                new[] { Front+3, Right+3, Back+3, Left+3 },
                new[] { Front+4, Right+4, Back+4, Left+4 },
                new[] { Front+5, Right+5, Back+5, Left+5 },
            };

            var cyclesR = new[] {
                new[] { Right+0, Right+2, Right+8, Right+6 },
                new[] { Right+1, Right+5, Right+7, Right+3 },
                new[] { Front+2, Up+2, Back+6, Down+2 },
                new[] { Front+5, Up+5, Back+3, Down+5 },
                new[] { Front+8, Up+8, Back+0, Down+8 },
            };

            var cyclesL = new[] {
                new[] { Left+0, Left+2, Left+8, Left+6 },
                new[] { Left+1, Left+5, Left+7, Left+3 },
                new[] { Front+0, Down+0, Back+8, Up+0 },
                new[] { Front+3, Down+3, Back+5, Up+3 },
                new[] { Front+6, Down+6, Back+2, Up+6 },
            };

            var cyclesM = new[] {
                new[] { Front+1, Down+1, Back+7, Up+1 },
                new[] { Front+4, Down+4, Back+4, Up+4 },
                new[] { Front+7, Down+7, Back+1, Up+7 },
            };


            var moves = new List<RawMove>
            {
                new RawMove("F", cyclesF),
                new RawMove("U", cyclesU),
                new RawMove("R", cyclesR),
                new RawMove("L", cyclesL),
                new RawMove("D", cyclesD),
                new RawMove("B", cyclesB),

                new RawMove("M", cyclesM),
                new RawMove("S", cyclesS),
                new RawMove("E", cyclesE),

                // Why in heck are slice moves so inconsistent in orientation
                new RawMove("Bw", cyclesB.Concatenate(cyclesS.Select(x => x.Reverse().ToArray()).ToArray())),
                new RawMove("Uw", cyclesU.Concatenate(cyclesE.Select(x => x.Reverse().ToArray()).ToArray())),
                new RawMove("Rw", cyclesR.Concatenate(cyclesM.Select(x => x.Reverse().ToArray()).ToArray())),
                new RawMove("Lw", cyclesL.Concatenate(cyclesM)),
                new RawMove("Dw", cyclesD.Concatenate(cyclesE)),
                new RawMove("Fw", cyclesF.Concatenate(cyclesS)),

                new RawMove("x", cyclesR.Concatenate(
                    cyclesM.Select(x => x.Reverse().ToArray()).ToArray(),
                    cyclesL.Select(x => x.Reverse().ToArray()).ToArray())),

                new RawMove("y", cyclesU.Concatenate(
                    cyclesE.Select(x => x.Reverse().ToArray()).ToArray(),
                    cyclesD.Select(x => x.Reverse().ToArray()).ToArray())),

                new RawMove("z", cyclesF.Concatenate(
                    cyclesS,
                    cyclesB.Select(x => x.Reverse().ToArray()).ToArray())),


                // I don't know what I'll do with these but it seemed fun to add algorithm shorthands

                new RawMove("Tperm", new[] {
                    new[] { Left+1, Right+1 }, new[] { Up+3, Up+5 }, // Edges
                    new[] { Front+2, Right+2 }, new[] { Back+0, Right+0 }, new[] { Up+2, Up+8 } // Corners
                }),

                new RawMove("sexy", new[] { // R U R' U'
                    new[] { Down+2, Front+2, Front+8, Up+8, Right+6, Right+0 }, // Corner swap and rotation
                    new[] { Right+2, Back+2, Back+0, Left+0, Up+2, Up+0 }, // Corner swap and rotation
                    new[] { Front+5, Up+5, Up+1 }, new[] { Right+3, Right+1, Back+1 }, // Edge cycle
                }),

                new RawMove("lsexy", new[] { // L' U' L U
                    new[] { Down+0, Front+0, Front+6, Up+6, Left+8, Left+2 }, // Corner swap and rotation
                    new[] { Left+0, Back+0, Back+2, Right+2, Up+0, Up+2 }, // Corner swap and rotation
                    new[] { Front+3, Up+3, Up+1 }, new[] { Left+5, Left+1, Back+1 }, // Edge cycle
                }),

                new RawMove("superflip", new[] { // Flip all edges
                    new[] { Front+1, Up+7 }, new[] { Front+5, Right+3 }, new[] { Front+7, Down+1 }, new[] { Front+3, Left+5 },
                    new[] { Back+1, Up+1 }, new[] { Back+5, Left+3 }, new[] { Back+7, Down+7 }, new[] { Back+3, Right+5 },
                    new[] { Right+1, Up+5 }, new[] { Right+7, Down+5 },
                    new[] { Left+1, Up+3 }, new[] { Left+7, Down+3 },
                }),
            };

            return moves.AsReadOnly();
        }
    }
}
