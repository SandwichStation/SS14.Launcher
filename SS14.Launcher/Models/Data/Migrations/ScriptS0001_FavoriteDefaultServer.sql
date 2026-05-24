INSERT INTO FavoriteServer (Address, Name, RaiseTime)
SELECT
    'ss14://station.sandwich14.com',                                                   -- Full Address
    '🥪[EN][MRP][18+]🥪 Sandwich Station - You bring the bread, we add the cheese!',  -- Display Name
    '1970-01-01 00:00:00'                                                              -- Last Seen
WHERE NOT EXISTS (
    SELECT 1 FROM FavoriteServer WHERE Address = 'ss14://station.sandwich14.com'
);
