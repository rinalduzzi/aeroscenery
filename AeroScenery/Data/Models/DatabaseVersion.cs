﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AeroScenery.Data.Models
{
    public class DatabaseVersion
    {
        public int DatabaseVersionId { get; set; }
        public string UpgradedOn { get; set; }
    }
}
