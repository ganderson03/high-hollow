using Crawler.Protocol;

namespace Crawler.Mock
{
    /// <summary>
    /// One self-contained rule set, switched on by a level. Levels list the mechanics they
    /// use; the core (movement, doors, levers, plates, keys, pits, the sightstone, the goal)
    /// is always on. The Rust server should keep the same boundaries so rule parity can be
    /// reviewed one mechanic at a time. See docs/levels-design.md.
    ///
    /// Ordering: within a tick, seats are processed in seat order, and mechanics are called
    /// in the order the level lists them.
    /// </summary>
    interface IMechanic
    {
        /// <summary>Called once the world has created its entities from the level's glyphs.</summary>
        void OnLoad(MockWorld w);

        /// <summary>True if this mechanic makes the cell impassable right now (a closed door).</summary>
        bool Blocks(MockWorld w, int x, int y);

        /// <summary>A seat has just stepped into a new cell.</summary>
        void OnEnter(MockWorld w, int seat, uint tick);

        /// <summary>A seat pressed interact. Return true if this mechanic handled it.</summary>
        bool OnInteract(MockWorld w, int seat, uint tick);

        /// <summary>Write this mechanic's entities' current states into the snapshot.</summary>
        void Write(MockWorld w, EntityState[] entities);

        /// <summary>Add what the given seat should not see, or should see falsely.</summary>
        void Perceive(MockWorld w, int seat, OverlayBuilder overlay);

        /// <summary>Put this mechanic back to the level's starting state.</summary>
        void Reset(MockWorld w);
    }
}
