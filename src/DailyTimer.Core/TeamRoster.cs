using System;
using System.Collections.Generic;

namespace DailyTimer.Core
{
    public sealed class RosterMember
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public int? PersonalSeconds { get; set; }
    }

    public sealed class TeamRoster
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public List<RosterMember> Members { get; set; }

        public TeamRoster()
        {
            Members = new List<RosterMember>();
        }
    }
}
