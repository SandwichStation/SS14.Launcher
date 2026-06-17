CREATE TABLE IF NOT EXISTS Hub (
    Address TEXT NOT NULL PRIMARY KEY,
    Priority INTEGER NOT NULL UNIQUE, -- 0 is highest priority

    -- Address can't be empty
    CONSTRAINT AddressNotEmpty CHECK (Address <> ''),
    -- Ensure priority is >= 0
    CONSTRAINT PriorityNotNegative CHECK (Priority >= 0)
);

INSERT INTO Hub (Address, Priority)
SELECT 'https://hub.playss14.com', 0
WHERE NOT EXISTS (
    SELECT 1 FROM Hub WHERE Address = 'https://hub.playss14.com'
);
