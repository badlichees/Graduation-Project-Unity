using System.Collections;

namespace RobotSimulation.MapGeneration
{
    public static class Utility
    {
        // 传入 seed 保证同一张地图在 Unity 和 ROS 调试中可复现
        public static T[] ShuffleArray<T>(T[] array, int seed)
        {
            System.Random prng = new System.Random(seed);

            for (int i = 0; i < array.Length - 1; i++)
            {
                int randomIndex = prng.Next(i, array.Length);
                T tempItem = array[randomIndex];
                array[randomIndex] = array[i];
                array[i] = tempItem;
            }

            return array;
        }
    }
}
