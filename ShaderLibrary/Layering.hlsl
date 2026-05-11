#if !defined(SLZ_LAYERING)
#define SLZ_LAYERING

namespace SLZ
{
namespace Layering
{
    struct Layer
    {
        min16int    index;
        min16float  weight;
        //min16float  uvScale;
    };

    void Swap(inout Layer items[3], int a, int b)
    {
        Layer temp  = items[a];
        items[a] = items[b];
        items[b] = temp;
    }

    void Swap(inout Layer items[4], int a, int b)
    {
        Layer temp  = items[a];
        items[a] = items[b];
        items[b] = temp;
    }

    void Swap(inout Layer items[5], int a, int b)
    {
        Layer temp  = items[a];
        items[a] = items[b];
        items[b] = temp;
    }

    // Sort 4 half2 pairs by the second element from largest to smallest
    void Sort4(inout Layer items[4])
    {
        if (items[0].weight < items[2].weight) Swap(items, 0, 2);
        if (items[1].weight < items[3].weight) Swap(items, 1, 3);
        if (items[0].weight < items[1].weight) Swap(items, 0, 1);
        if (items[2].weight < items[3].weight) Swap(items, 2, 3);
        if (items[1].weight < items[2].weight) Swap(items, 1, 2);
    }

    // Partially Sort 4 half2 pairs by the second element such that the largest two are first
    void PartialSort4(inout Layer items[4])
    {
        if (items[0].weight < items[3].weight) Swap(items, 0, 1);
        if (items[1].weight < items[2].weight) Swap(items, 2, 3);
        if (items[1].weight < items[3].weight) Swap(items, 1, 3);
        if (items[0].weight < items[2].weight) Swap(items, 1, 3);
    }

    // Partially Sort 5 half2 pairs by the second element such that the largest three are first
    void PartialSort5(inout Layer items[5])
    {
        if (items[0].weight < items[3].weight) Swap(items, 0, 3);
        if (items[1].weight < items[4].weight) Swap(items, 1, 4);

        if (items[2].weight < items[4].weight) Swap(items, 2, 4);
        if (items[1].weight < items[3].weight) Swap(items, 1, 3);

        if (items[3].weight < items[4].weight) Swap(items, 3, 4);
        if (items[0].weight < items[2].weight) Swap(items, 0, 2);

        //if (items[0].weight < items[1].weight) Swap(items, 0, 1);
        if (items[2].weight < items[3].weight) Swap(items, 2, 3);

        //if (items[1].weight < items[2].weight) Swap(items, 1, 2);
    }

    // Fully sort 5 half2 pairs by the second element in descending order using a sorting network
    // upside down version of https://web.archive.org/web/20220704212019/http://users.telenet.be/bertdobbelaere/SorterHunter/sorting_networks.html#N5L9D5
    // 0 --*---------------*----*-------
    //     |               |    |       
    // 1 --|--*-------*----|----*----*--
    //     |  |       |    |         |
    // 2 --|--|----*--|----*----*----*--
    //     |  |    |  |         |       
    // 3 --*--|----|--*----*----*-------
    //        |    |       |            
    // 4 -----*----*-------*------------
    void Sort5(inout Layer items[5])
    {
        if (items[0].weight < items[3].weight) Swap(items, 0, 3);
        if (items[1].weight < items[4].weight) Swap(items, 1, 4);

        if (items[2].weight < items[4].weight) Swap(items, 2, 4);
        if (items[1].weight < items[3].weight) Swap(items, 1, 3);

        if (items[3].weight < items[4].weight) Swap(items, 3, 4);
        if (items[0].weight < items[2].weight) Swap(items, 0, 2);

        if (items[0].weight < items[1].weight) Swap(items, 0, 1);
        if (items[2].weight < items[3].weight) Swap(items, 2, 3);

        if (items[1].weight < items[2].weight) Swap(items, 1, 2);
    }

    void GetThreeActiveLayers(half baseWeight, half4 weights, out Layer layers[3])
    {
        Layer allLayers[5] = 
        {
            {min16int(0), baseWeight},
            {min16int(1), weights.x},
            {min16int(2), weights.y},
            {min16int(3), weights.z},
            {min16int(4), weights.w}
        };
        Sort5(allLayers);
        /*
        // Sort by index so that order of samples is not divergent between threads
        if (items[0].index > items[2].index) Swap(items, 0, 2);
        if (items[0].index > items[1].index) Swap(items, 0, 1);
        if (items[1].index > items[2].index) Swap(items, 1, 2);
        */

        layers[0] = allLayers[0];
        layers[1] = allLayers[1];
        layers[2] = allLayers[2];
    }

    void GetThreeActiveLayersNoBase(half4 weights, out Layer layers[3])
    {
        Layer allLayers[4] = 
        {
            {min16int(1), weights.x},
            {min16int(2), weights.y},
            {min16int(3), weights.z},
            {min16int(4), weights.w}
        };
        Sort4(allLayers);
        /*
        // Sort by index so that order of samples is not divergent between threads
        if (items[0].index > items[2].index) Swap(items, 0, 2);
        if (items[0].index > items[1].index) Swap(items, 0, 1);
        if (items[1].index > items[2].index) Swap(items, 1, 2);
        */

        layers[0] = allLayers[0];
        layers[1] = allLayers[1];
    }
}
}
#endif