﻿using Mapping_Tools.Classes.MathUtil;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mapping_Tools.Classes.HitsoundStuff {
    class HitsoundConverter {
        public static List<SamplePackage> ZipLayers(IEnumerable<HitsoundLayer> layers, Sample defaultSample, double leniency=15, bool needNormalSample=true) {
            List<SamplePackage> packages = new List<SamplePackage>();
            foreach (HitsoundLayer hl in layers) {
                foreach (double t in hl.Times) {
                    SamplePackage packageOnTime = packages.Find(o => Math.Abs(o.Time - t) <= leniency);
                    if (packageOnTime != null) {
                        packageOnTime.Samples.Add(new Sample(hl));
                    } else {
                        packages.Add(new SamplePackage(t, new HashSet<Sample> { new Sample(hl) }));
                    }
                }
            }

            if (needNormalSample) {
                // Packages without a hitnormal sample
                foreach (SamplePackage p in packages.Where(o => o.Samples.All(s => s.Hitsound != 0))) {
                    p.Samples.Add(defaultSample.Copy());
                }
            }

            packages = packages.OrderBy(o => o.Time).ToList();
            return packages;
        }

        /// <summary>
        /// Balances the volume of <see cref="SamplePackage"/> such that volume is mostly handled by osu!'s volume controllers rather than
        /// in-sample amplitude changes.
        /// </summary>
        /// <param name="packages"></param>
        /// <param name="roughness">Quantizing level in the new volumes of samples. Can be used to decrease the number of distinct volume levels.</param>
        /// <param name="alwaysFullVolume">Forces to always use maximum amplitude in the samples.</param>
        /// <param name="individualVolume">Allows for multiple distinct volume levels within a single <see cref="SamplePackage"/>.</param>
        public static void BalanceVolumes(IEnumerable<SamplePackage> packages, double roughness, bool alwaysFullVolume, bool individualVolume=false) {
            foreach (SamplePackage package in packages) {
                if (individualVolume) {
                    // Simply mix the volume in the sample to the outside volume
                    foreach (Sample sample in package.Samples) {
                        sample.OutsideVolume = SampleImporter.AmplitudeToVolume(
                            SampleImporter.VolumeToAmplitude(sample.OutsideVolume) *
                            SampleImporter.VolumeToAmplitude(sample.SampleArgs.Volume));
                        sample.SampleArgs.Volume = 1;
                    }
                    continue;
                }

                double maxVolume = package.Samples.Max(o => o.SampleArgs.Volume);
                if (Math.Abs(maxVolume - -0.01) < Precision.DOUBLE_EPSILON) {
                    maxVolume = 1;
                }

                foreach (Sample sample in package.Samples) {
                    if (Math.Abs(sample.SampleArgs.Volume - -0.01) < Precision.DOUBLE_EPSILON) {
                        sample.SampleArgs.Volume = 1;
                    }

                    // Pick the new volume such that the samples have a volume as high as possible and the greenline brings the volume down.
                    // With this equation the final amplitude stays the same while the greenline has the volume of the loudest sample at this time.
                    double newVolume = SampleImporter.AmplitudeToVolume(
                        SampleImporter.VolumeToAmplitude(sample.OutsideVolume) *
                        SampleImporter.VolumeToAmplitude(sample.SampleArgs.Volume) /
                        SampleImporter.VolumeToAmplitude(maxVolume));


                    if (Math.Abs(newVolume - 1) > roughness && !alwaysFullVolume) {
                        // If roughness is not 0 it will quantize the new volume in order to reduce the number of different volumes
                        sample.SampleArgs.Volume = Math.Abs(roughness) > Precision.DOUBLE_EPSILON ? 
                            roughness * Math.Round(newVolume / roughness) : 
                            newVolume;
                    } else {
                        sample.SampleArgs.Volume = 1;
                    }
                }

                if (alwaysFullVolume) {
                    // Assuming the volume of the sample is always maximum, this equation makes sure that 
                    // the loudest sample at this time has the wanted amplitude using the volume change from the greenline.
                    package.SetAllOutsideVolume(SampleImporter.AmplitudeToVolume(
                        SampleImporter.VolumeToAmplitude(package.MaxOutsideVolume) *
                        SampleImporter.VolumeToAmplitude(maxVolume)));
                } else {
                    package.SetAllOutsideVolume(maxVolume);
                }
            }
        }

        public static List<CustomIndex> GetCustomIndices(List<SamplePackage> packages, Dictionary<SampleGeneratingArgs, SampleSoundGenerator> loadedSamples = null) {
            var indices = packages.Select(o => o.GetCustomIndex()).ToList();
            indices.ForEach(o => o.CleanInvalids(loadedSamples));
            return indices;
        }

        /// <summary>
        /// Makes a new smaller list of CustomIndices which still fits every CustomIndex
        /// </summary>
        /// <param name="customIndices">The CustomIndices that it has to support</param>
        /// <returns></returns>
        public static List<CustomIndex> OptimizeCustomIndices(List<CustomIndex> customIndices) {
            List<CustomIndex> newCustomIndices = new List<CustomIndex>();

            // Try merging together CustomIndices as much as possible
            foreach (CustomIndex ci in customIndices) {
                CustomIndex mergingWith = newCustomIndices.Find(o => o.CanMerge(ci));

                if (mergingWith != null) {
                    mergingWith.MergeWith(ci);
                } else {
                    // There is no CustomIndex to merge with so add a new one
                    newCustomIndices.Add(ci.Copy());
                }
            }

            // Remove any CustomIndices that might be obsolete
            newCustomIndices.RemoveAll(o => !IsUseful(o, newCustomIndices.Except(new CustomIndex[] { o }).ToList(), customIndices));

            return newCustomIndices;
        }

        private static bool IsUseful(CustomIndex subject, List<CustomIndex> otherCustomIndices, List<CustomIndex> supportedCustomIndices) {
            // Subject is useful if it can fit a CustomIndex that no other can fit
            if (supportedCustomIndices.Any(ci => subject.Fits(ci) && !otherCustomIndices.Any(o => o.Fits(ci)))) {
                return true;
            }
            return false;
        }

        public static void GiveCustomIndicesIndices(List<CustomIndex> customIndices) {
            for (int i = 0; i < customIndices.Count; i++) {
                customIndices[i].Index = i + 1;  // osu! CustomIndices start from 1
            }
        }

        public static List<HitsoundEvent> GetHitsounds(List<SamplePackage> samplePackages,
            ref Dictionary<SampleGeneratingArgs, SampleSoundGenerator> loadedSamples,
            ref Dictionary<SampleGeneratingArgs, string> names,
            ref Dictionary<SampleGeneratingArgs, Vector2> positions) {

            HashSet<SampleGeneratingArgs> allSampleArgs = new HashSet<SampleGeneratingArgs>();
            foreach (SamplePackage sp in samplePackages) {
                allSampleArgs.UnionWith(sp.Samples.Select(o => o.SampleArgs));
            }

            if (loadedSamples == null) {
                loadedSamples = SampleImporter.ImportSamples(allSampleArgs);
            }

            if (names == null) {
                names = HitsoundExporter.GenerateSampleNames(allSampleArgs, loadedSamples);
            }

            if (positions == null) {
                positions = HitsoundExporter.GenerateHitsoundPositions(allSampleArgs);
            }

            var hitsounds = new List<HitsoundEvent>();
            foreach (var p in samplePackages) {
                foreach (var s in p.Samples) {
                    hitsounds.Add( new HitsoundEvent(p.Time,
                        positions[s.SampleArgs], s.OutsideVolume, names[s.SampleArgs], s.SampleSet, s.SampleSet,
                        0, s.Whistle, s.Finish, s.Clap));
                }
            }

            return hitsounds;
        }

        /// <summary>
        /// Generates 1-to-1 <see cref="HitsoundEvent"/> of out <see cref="SamplePackage"/> using provided custom indices.
        /// </summary>
        /// <param name="samplePackages">The SamplePackages to get hitsounds out of</param>
        /// <param name="customIndices">The CustomIndices that fit all the packages</param>
        /// <returns></returns>
        public static List<HitsoundEvent> GetHitsounds(List<SamplePackage> samplePackages, List<CustomIndex> customIndices) {
            List<HitsoundEvent> hitsounds = new List<HitsoundEvent>(samplePackages.Count);
            List<CustomIndex> packageCustomIndices = GetCustomIndices(samplePackages);

            int index = 0;
            while (index < packageCustomIndices.Count) {
                // Find CustomIndex that fits the most packages from here
                CustomIndex bestCustomIndex = null;
                int bestFits = 0;

                foreach (CustomIndex ci in customIndices) {
                    int fits = NumSupportedPackages(packageCustomIndices, index, ci);

                    if (fits > bestFits) {
                        bestCustomIndex = ci;
                        bestFits = fits;
                    }
                }


                if (bestFits == 0) {
                    throw new Exception("Custom indices can't fit the sample packages.");
                } else {
                    // Add all the fitted packages as hitsounds
                    for (int i = 0; i < bestFits; i++)
                    {
                        if (bestCustomIndex != null)
                            hitsounds.Add(samplePackages[index + i].GetHitsound(bestCustomIndex.Index));
                    }
                    index += bestFits;
                }
            }
            return hitsounds;
        }

        private static int NumSupportedPackages(List<CustomIndex> packageCustomIndices, int i, CustomIndex ci) {
            int supported = 0;
            int index = i;
            while (index < packageCustomIndices.Count) {
                if (ci.Fits(packageCustomIndices[index++])) {
                    supported++;
                } else {
                    return supported;
                }
            }
            return supported;
        }

        public static CompleteHitsounds GetCompleteHitsounds(List<SamplePackage> packages, Dictionary<SampleGeneratingArgs, SampleSoundGenerator> loadedSamples = null) {
            var customIndices = OptimizeCustomIndices(GetCustomIndices(packages, loadedSamples));
            GiveCustomIndicesIndices(customIndices);

            var hitsounds = GetHitsounds(packages, customIndices);

            return new CompleteHitsounds(hitsounds, customIndices);
        }
    }
}
