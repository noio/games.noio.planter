using System;
using System.Collections.Generic;
using System.Linq;
using Random = UnityEngine.Random;

namespace games.noio.planter.Utils
{
    public static class Utils
    {
        /// <summary>
        ///     Pick a random item from the list, with
        ///     each item having a specified weight
        /// </summary>
        /// <param name="sequence"></param>
        /// <param name="weightSelector"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T PickWeighted<T>(IList<T> sequence, Func<T, float> weightSelector)
        {
            var totalWeight = sequence.Sum(weightSelector);
            if (totalWeight <= 0)
            {
                return sequence[0];
            }

            // The weight we are after...
            var itemWeightIndex = Random.value * totalWeight;
            float currentWeightIndex = 0;
            foreach (var item in sequence)
            {
                currentWeightIndex += weightSelector(item);

                // If we've hit or passed the weight we are after for this item then it's the one we want....
                if (currentWeightIndex >= itemWeightIndex)
                {
                    return item;
                }
            }

            return default;
        }
    }
}