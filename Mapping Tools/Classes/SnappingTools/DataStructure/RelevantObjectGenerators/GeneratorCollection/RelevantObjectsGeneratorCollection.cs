﻿using System.Collections.Generic;
using System.Linq;

namespace Mapping_Tools.Classes.SnappingTools.DataStructure.RelevantObjectGenerators.GeneratorCollection {
    public class RelevantObjectsGeneratorCollection : List<RelevantObjectsGenerator> {
        public RelevantObjectsGeneratorCollection(IEnumerable<RelevantObjectsGenerator> collection) : base(collection) {}

        public IEnumerable<RelevantObjectsGenerator> GetActiveGenerators() {
            return this.Where(o => o.IsActive);
        }
    }
}