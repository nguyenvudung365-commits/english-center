-- Run once against the isolated local database after backing it up. Keep all IDs and storage object keys.
BEGIN;
CREATE TEMP TABLE local_class_rename ON COMMIT DROP AS
SELECT code AS old_code, substring(code FROM 6) AS new_code FROM academic.classes
WHERE code IN ('DEMO-FND-A1','DEMO-FND-A2','DEMO-FND-S1','DEMO-FND-N1','DEMO-INT-B1',
               'DEMO-INT-B2','DEMO-INT-S1','DEMO-INT-F0','DEMO-ADV-C1','DEMO-ADV-C2');
DO $$ BEGIN
  IF EXISTS (SELECT FROM local_class_rename m JOIN academic.classes c ON c.code = m.new_code) THEN
    RAISE EXCEPTION 'Destination class code already exists';
  END IF;
END $$;
CREATE TEMP TABLE local_user_rename ON COMMIT DROP AS
SELECT "Id", "Email" AS old_email, regexp_replace("Email", '@demo\.english-center\.invalid$', '@lms.english-center.invalid', 'i') AS new_email
FROM identity.users WHERE "Email" ~* '@demo\.english-center\.invalid$';
UPDATE identity.users u SET "Email" = m.new_email, "NormalizedEmail" = upper(m.new_email),
  "FullName" = CASE WHEN u."FullName" = 'Demo Administrator' THEN 'LMS Administrator' ELSE u."FullName" END,
  "Revision" = gen_random_uuid(), "ProfileVersion" = u."ProfileVersion" + 1
FROM local_user_rename m WHERE u."Id" = m."Id";
UPDATE identity.refresh_sessions s SET "RevokedAt" = now() FROM local_user_rename m
WHERE s."UserId" = m."Id" AND s."RevokedAt" IS NULL;
UPDATE academic.teachers t SET email = u."Email", full_name = u."FullName", profile_version = u."ProfileVersion"
FROM local_user_rename m JOIN identity.users u ON u."Id" = m."Id" WHERE t.id = m."Id";
UPDATE enrollment.students s SET email = u."Email" FROM local_user_rename m JOIN identity.users u ON u."Id" = m."Id"
WHERE s.student_id = m."Id";
UPDATE academic.classes c SET code = m.new_code FROM local_class_rename m WHERE c.code = m.old_code;
UPDATE enrollment.class_snapshots c SET class_code = m.new_code FROM local_class_rename m WHERE c.class_code = m.old_code;
CREATE FUNCTION pg_temp.clean_local_label(label text) RETURNS text LANGUAGE sql IMMUTABLE AS $$
  SELECT CASE WHEN label = 'chrome-demo.pdf' THEN 'chrome-qa.pdf'
              ELSE regexp_replace(label, 'demo-((fnd|int|adv)-[a-z][0-9])', '\1', 'gi') END
$$;
UPDATE academic.assignments SET title = pg_temp.clean_local_label(title), prompt_file_name = pg_temp.clean_local_label(prompt_file_name)
WHERE title IS DISTINCT FROM pg_temp.clean_local_label(title) OR prompt_file_name IS DISTINCT FROM pg_temp.clean_local_label(prompt_file_name);
UPDATE academic.class_materials SET title = pg_temp.clean_local_label(title), file_name = pg_temp.clean_local_label(file_name)
WHERE title IS DISTINCT FROM pg_temp.clean_local_label(title) OR file_name IS DISTINCT FROM pg_temp.clean_local_label(file_name);
UPDATE academic.submission_files SET file_name = pg_temp.clean_local_label(file_name)
WHERE file_name IS DISTINCT FROM pg_temp.clean_local_label(file_name);
UPDATE media_notification.stored_files SET "FileName" = pg_temp.clean_local_label("FileName")
WHERE "FileName" IS DISTINCT FROM pg_temp.clean_local_label("FileName");
UPDATE media_notification.class_posts SET "Content" = pg_temp.clean_local_label("Content")
WHERE "Content" IS DISTINCT FROM pg_temp.clean_local_label("Content");
UPDATE media_notification.post_comments SET "Content" = pg_temp.clean_local_label("Content")
WHERE "Content" IS DISTINCT FROM pg_temp.clean_local_label("Content");
UPDATE enrollment.placement_tests SET test_name = replace(test_name, 'IELTS DEMO-', 'IELTS ')
WHERE test_name LIKE 'Kiểm tra đầu vào IELTS DEMO-%';
COMMIT;
