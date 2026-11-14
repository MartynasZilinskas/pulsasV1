-- Synthetic rectangles, NOT real administrative borders. Safe to rerun.
BEGIN;
SELECT pg_advisory_xact_lock(1347767379, 1);
INSERT INTO public.regions ("Id", "Code", "CountryCode", "Name", "Shape") VALUES
('a0000000-0000-4000-8000-000000000001', 'DVA', 'LT', 'DEMO west (synthetic)',
 ST_Multi(ST_MakeEnvelope(24,54,25,55,4326))),
('a0000000-0000-4000-8000-000000000002', 'DVB', 'LT', 'DEMO east (synthetic)',
 ST_Multi(ST_MakeEnvelope(25,54,26,55,4326)))
ON CONFLICT ("Id") DO UPDATE SET "Shape" = EXCLUDED."Shape", "Name" = EXCLUDED."Name";

INSERT INTO public.node_information ("Id", "PublicKey", "SourceType", "Name", "Location") VALUES
('b0000000-0000-4000-8000-000000000001', decode(repeat('d1',32),'hex'), 'unknown', 'DEMO inside west', ST_SetSRID(ST_MakePoint(24.5,54.5),4326)),
('b0000000-0000-4000-8000-000000000002', decode(repeat('d2',32),'hex'), 'unknown', 'DEMO inside east', ST_SetSRID(ST_MakePoint(25.5,54.5),4326)),
('b0000000-0000-4000-8000-000000000003', decode(repeat('d3',32),'hex'), 'unknown', 'DEMO shared boundary', ST_SetSRID(ST_MakePoint(25,54.5),4326)),
('b0000000-0000-4000-8000-000000000004', decode(repeat('d4',32),'hex'), 'unknown', 'DEMO outside regions', ST_SetSRID(ST_MakePoint(27,54.5),4326)),
('b0000000-0000-4000-8000-000000000005', decode(repeat('d5',32),'hex'), 'unknown', 'DEMO missing location', NULL)
ON CONFLICT ("Id") DO UPDATE SET "Location" = EXCLUDED."Location", "Name" = EXCLUDED."Name";
COMMIT;

SELECT n."Name", ST_AsText(n."Location") AS location, r."Code" AS region
FROM public.node_information n
LEFT JOIN public.regions r ON r."Id" = n."LocationRegionId"
WHERE n."Id" IN (
 'b0000000-0000-4000-8000-000000000001', 'b0000000-0000-4000-8000-000000000002',
 'b0000000-0000-4000-8000-000000000003', 'b0000000-0000-4000-8000-000000000004',
 'b0000000-0000-4000-8000-000000000005')
ORDER BY n."Id";
