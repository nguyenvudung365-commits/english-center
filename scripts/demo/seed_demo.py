#!/usr/bin/env python3
"""Repeatable rich demo seed for an ISOLATED, loopback-only local stack.

Stdlib only. Seed data is created through the local HTTP API and object storage.
Never prints passwords, tokens or signed URLs. Run `python scripts/demo/seed_demo.py --help`.
"""

import argparse
import http.client
import json
import os
import random
import secrets
import subprocess
import sys
import time
import unicodedata
from datetime import date, datetime, time as dtime, timedelta, timezone
from pathlib import Path
from urllib.parse import quote, urlencode, urlparse

ROOT = Path(__file__).resolve().parents[2]
ENV_FILE = ROOT / ".env.demo"            # ignored by .gitignore (`.env.*`)
STATE_FILE = ROOT / ".env.demo-seed.json"  # id cache only, same ignore rule
PROJECT = "english-center-demo"
MARKER = "english-center-demo"
VN = timezone(timedelta(hours=7))
MAIL = "lms.english-center.invalid"
PHASES = ["accounts", "classes", "sessions", "enrollment", "finance", "placement", "attendance",
          "practice", "coursework", "feed", "feedback", "faq", "notifications", "worksheets"]
LOGIN_GAP = 3.2  # identity-auth limiter is 20 requests/minute/IP


class Stop(Exception):
    pass


# ------------------------------------------------------------------ demo data (deterministic)

STAFF = ["Trần Minh Châu", "Lê Thu Hà", "Phạm Quốc Bảo", "Võ Ngọc Diệp"]
TEACHERS = ["Nguyễn Thị Lan Anh", "Trần Quang Huy", "Lê Hoàng Yến", "Phạm Đức Minh", "Vũ Thanh Hương",
            "Đặng Gia Bảo", "Bùi Khánh Linh", "Hoàng Anh Tuấn", "Ngô Mai Phương", "Đỗ Thế Vinh"]
N_STUDENTS = 48          # 0..41 enrolled in classes, 42..47 applicants (placement tests only)
N_ENROLLABLE = 42
SURNAMES = ["Nguyễn", "Trần", "Lê", "Phạm", "Hoàng", "Huỳnh", "Phan", "Vũ", "Võ", "Đặng", "Bùi", "Đỗ", "Hồ", "Ngô"]
MALE = ["An", "Bảo", "Cường", "Dũng", "Duy", "Hải", "Hiếu", "Hoàng", "Huy", "Khang", "Khoa", "Long", "Nam",
        "Phúc", "Quân", "Sơn", "Thành", "Tuấn", "Việt", "Vinh", "Đạt", "Tài"]
FEMALE = ["Anh", "Châu", "Dung", "Giang", "Hà", "Hạnh", "Hương", "Lan", "Linh", "Mai", "My", "Ngân", "Nhi",
          "Phương", "Quỳnh", "Thảo", "Trang", "Trinh", "Uyên", "Vy", "Yến", "Thư"]

TOPICS = {
    "IELTS-FOUNDATION": ["Listening: Form Completion", "Reading: True/False/Not Given",
                         "Writing Task 1: Describing a Chart", "Speaking Part 1: Daily Life"],
    "IELTS-INTERMEDIATE": ["Listening: Map Labelling", "Reading: Matching Headings",
                           "Writing Task 2: Opinion Essay", "Speaking Part 2: Describe a Person"],
    "IELTS-ADVANCED": ["Listening: Multiple Choice Traps", "Reading: Summary Completion",
                       "Writing Task 2: Discussion Essay", "Speaking Part 3: Abstract Ideas"],
}
SKILL_ROTATION = [["Listening"], ["Reading"], ["Writing"], ["Speaking"], ["Listening", "Reading"],
                  ["Writing", "Speaking"]]

# first student = 5*k (mod 42); `n` consecutive students; revoke = last member loses class access.
CLASSES = [
    dict(code="FND-A1", name="IELTS Foundation A1 - Tối 2-4-6", course="IELTS-FOUNDATION", type="Main",
         start=-21, end=42, teacher=1, at=(18, 0), max=12, fee=4200000, n=9),
    dict(code="FND-A2", name="IELTS Foundation A2 - Sáng cuối tuần", course="IELTS-FOUNDATION", type="Main",
         start=-14, end=49, teacher=2, at=(8, 0), max=12, fee=4200000, n=9, revoke_last=True),
    dict(code="FND-S1", name="Foundation phụ đạo Listening - Reading", course="IELTS-FOUNDATION",
         type="Supplementary", start=-7, end=28, teacher=3, at=(19, 30), max=10, fee=1200000, n=7),
    dict(code="INT-B1", name="IELTS Intermediate B1 - Tối thường ngày", course="IELTS-INTERMEDIATE", type="Main",
         start=-28, end=35, teacher=4, at=(18, 30), max=12, fee=6800000, n=9, revoke_last=True),
    dict(code="INT-B2", name="IELTS Intermediate B2 - Lớp đã đầy", course="IELTS-INTERMEDIATE", type="Main",
         start=-10, end=50, teacher=5, at=(9, 0), max=8, fee=6800000, n=8),
    dict(code="INT-S1", name="Intermediate Writing Clinic", course="IELTS-INTERMEDIATE",
         type="Supplementary", start=-5, end=30, teacher=6, at=(20, 0), max=10, fee=1800000, n=7),
    dict(code="ADV-C1", name="IELTS Advanced C1 - Mục tiêu 6.5+", course="IELTS-ADVANCED", type="Main",
         start=-30, end=30, teacher=7, at=(18, 0), max=10, fee=9500000, n=8),
    dict(code="ADV-C2", name="IELTS Advanced C2 - Khai giảng tuần sau", course="IELTS-ADVANCED", type="Main",
         start=7, end=84, teacher=8, at=(19, 0), max=12, fee=9500000, n=5),
    dict(code="FND-N1", name="IELTS Foundation N1 - Khai giảng sau 2 tuần", course="IELTS-FOUNDATION",
         type="Main", start=14, end=84, teacher=9, at=(8, 30), max=12, fee=4200000, n=5),
    dict(code="INT-F0", name="Intermediate K-1 (đã kết thúc)", course="IELTS-INTERMEDIATE", type="Main",
         start=-75, end=-8, teacher=10, at=(18, 0), max=10, fee=6800000, n=0),
]
for _k, _c in enumerate(CLASSES):
    _c["k"] = _k

FAQS = [
    ("Làm sao để đăng ký học tại trung tâm?",
     "Bạn liên hệ giáo vụ để được tư vấn, làm bài kiểm tra đầu vào (nếu cần) rồi chọn lớp phù hợp. Giáo vụ sẽ ghi danh "
     "và hệ thống tạo hóa đơn học phí cho bạn.", True),
    ("Bài kiểm tra đầu vào diễn ra như thế nào?",
     "Giáo vụ đặt lịch kiểm tra tại trung tâm hoặc trực tuyến. Trạng thái lịch gồm: Chờ thi, Đã thi, Đã hủy. "
     "Kết quả được giáo vụ dùng để xếp bạn vào lớp phù hợp.", True),
    ("Học phí được thanh toán và xác nhận ra sao?",
     "Sau khi ghi danh, hóa đơn được tạo với hạn thanh toán 7 ngày. Khi bạn đã đóng học phí, giáo vụ xác nhận trên hệ "
     "thống và bạn nhận thông báo 'Hóa đơn đã được xác nhận thanh toán'.", True),
    ("Quy định về điểm danh và nghỉ học?",
     "Giáo viên điểm danh trong buổi học và còn chỉnh sửa được đến 24 giờ sau khi buổi học kết thúc. Mỗi học viên có "
     "giới hạn số buổi nghỉ không phép, số lần đi trễ và số bài bỏ do giáo vụ thiết lập khi ghi danh.", True),
    ("Tôi nộp bài tập ở đâu và khi nào có điểm?",
     "Vào mục Bài tập của lớp, chọn bài đang mở và tải lên tối đa 5 tệp (PDF, DOCX, MP3, M4A, JPG, PNG). Giáo viên chấm "
     "điểm thang 10, bạn nhận thông báo khi bài được chấm.", True),
    ("Tôi có thể xem tiến độ học của mình ở đâu?",
     "Mục Thống kê cá nhân cho biết số buổi đã học, tỷ lệ chuyên cần theo tháng và số lớp đã hoàn thành.", True),
    ("Tôi muốn góp ý về lớp học hoặc dịch vụ thì làm thế nào?",
     "Dùng mục Góp ý, nhập tiêu đề, nội dung và đánh giá 1-5 sao. Giáo vụ sẽ chuyển trạng thái xử lý: Mới, Đang xử lý, "
     "Đã xử lý.", True),
    ("Bảng tin lớp dùng để làm gì?",
     "Giáo viên, giáo vụ và học viên của lớp trao đổi, thích và bình luận bài đăng. Nội dung không phù hợp có thể bị "
     "giáo viên hoặc giáo vụ ẩn.", True),
    ("[Nháp] Chính sách bảo lưu học phí", "Đang chờ ban giám đốc duyệt nội dung. Chưa công bố.", False),
    ("[Nháp] Học bổng cho học viên xuất sắc", "Đang soạn thảo, chưa công bố.", False),
]

FEEDBACKS = [  # (student idx, title, content, rating, target status)
    (0, "Cảm ơn cô về buổi học Listening", "Buổi Listening rất dễ hiểu, em tiến bộ rõ.", 5, "Resolved"),
    (3, "Phòng học hơi nóng", "Buổi tối phòng học nóng, mong trung tâm kiểm tra điều hòa.", 3, "Resolved"),
    (6, "Xin thêm bài tập Speaking", "Em muốn có thêm bài luyện Speaking part 2 mỗi tuần.", 4, "InProgress"),
    (9, "Lịch học bị trùng giờ thi", "Buổi học tuần sau trùng lịch thi ở trường, em xin hỏi cách sắp xếp.", 3, "InProgress"),
    (12, "Hóa đơn chưa hiện trạng thái", "Em đã đóng học phí nhưng hóa đơn vẫn ghi chưa thanh toán.", 2, "InProgress"),
    (15, "Tài liệu Reading hơi khó", "Bài Reading tuần này khó hơn trình độ lớp, mong giáo viên giải thích thêm.", 3, "New"),
    (18, "Đề xuất thêm buổi chữa đề", "Lớp mong có thêm buổi chữa đề mock test cuối tháng.", 4, "New"),
    (21, "Wifi tại trung tâm chậm", "Wifi chậm khi học Listening online.", 2, "New"),
    (24, "Giáo viên nhiệt tình", "Cô rất nhiệt tình và nhận xét bài chi tiết.", 5, "New"),
    (27, "Cần thêm chỗ để xe", "Giờ cao điểm chỗ để xe đã đầy.", 3, "New"),
    (30, "Lớp đông, khó hỏi bài", "Lớp 10 bạn nhưng giờ hỏi bài còn ít, mong có giờ office hour.", 3, "Resolved"),
    (33, "Đánh giá khóa Foundation", "Nội dung khóa phù hợp người mới, nhịp học vừa phải.", 5, "New"),
]

PLACEMENTS = [  # (student idx, seq, days from today, mode, status)
    (42, 1, 3, "Offline", "Pending"), (43, 1, 5, "Online", "Pending"), (44, 1, -4, "Offline", "Done"),
    (45, 1, -2, "Online", "Cancelled"), (46, 1, -10, "Offline", "Done"), (46, 2, 9, "Offline", "Pending"),
    (47, 1, 1, "Online", "Pending"),
    (0, 1, -40, "Offline", "Done"), (1, 1, -38, "Online", "Done"), (2, 1, -35, "Offline", "Done"),
    (3, 1, -33, "Offline", "Done"), (4, 1, -30, "Online", "Done"), (5, 1, -28, "Offline", "Done"),
]


def student_name(i):
    sur = SURNAMES[(i * 3) % len(SURNAMES)]
    if i % 2 == 0:
        return f"{sur} {['Văn', 'Minh', 'Quốc', 'Gia', 'Đức'][(i // 2) % 5]} {MALE[(i // 2 * 7) % len(MALE)]}"
    return f"{sur} {['Thị', 'Ngọc', 'Thu', 'Bảo', 'Khánh'][(i // 2) % 5]} {FEMALE[(i // 2 * 7) % len(FEMALE)]}"


def staff_email(n): return f"staff{n:02d}@{MAIL}"
def teacher_email(n): return f"gv{n:02d}@{MAIL}"
def student_email(i): return f"hv{i:02d}@{MAIL}"


def members(cls):
    first = (5 * cls["k"]) % N_ENROLLABLE
    return [(first + j) % N_ENROLLABLE for j in range(cls["n"])]


def revoked(cls):
    return members(cls)[-1:] if cls.get("revoke_last") else []


def warning_example(cls, student):
    return cls["k"] == 0 and student == members(cls)[0]


def chance(p, *key):
    return random.Random("|".join(map(str, key))).random() < p


def pick(options, *key):
    return random.Random("|".join(map(str, key))).choice(options)


# ------------------------------------------------------------------ time helpers

def iso(dt): return dt.astimezone(VN).isoformat(timespec="seconds")
def parse_ts(text): return datetime.fromisoformat(text.replace("Z", "+00:00"))
def at(day, hour, minute=0): return datetime.combine(day, dtime(hour, minute), VN)


def vn_today(now): return now.astimezone(VN).date()


def session_plan(cls, start, end, today, now):
    """Desired sessions for one class: rolling window around today, plus one attendance-writable 'live' session.

    Attendance can only be written from session start until 24h after its end, so the live session is anchored
    to the real clock (two hours before the current hour) and keyed by its unit label, not by time.
    """
    live = None
    if start <= today - timedelta(days=2) and end >= today:
        hour = now.astimezone(VN).replace(minute=0, second=0, microsecond=0)
        ls, le = hour - timedelta(hours=2), hour - timedelta(hours=1)
        if ls.date() >= start and le <= at(end + timedelta(days=1), 0):
            live = dict(unit=f"Live {ls:%d/%m}", lesson="Buổi học gần nhất: luyện đề và điểm danh",
                        skills=["Listening", "Speaking"], type="Regular", exam=None, starts=ls, ends=le)
    regular = []
    day = max(start, today - timedelta(days=14))
    last = min(end, today + timedelta(days=21))
    topics = TOPICS[cls["course"]]
    while day <= last:
        n = (day - start).days
        if n % 2 == 0:
            idx = n // 2 + 1
            test = idx % 8 == 0
            begin = at(day, *cls["at"])
            finish = begin + timedelta(minutes=120 if test else 90)
            clash = live and begin < live["ends"] + timedelta(minutes=15) and finish > live["starts"] - timedelta(minutes=15)
            if not clash:
                regular.append(dict(
                    unit=f"Unit {(idx - 1) // 4 + 1}", lesson=f"Lesson {idx}: {topics[idx % len(topics)]}",
                    skills=["Listening", "Reading", "Writing", "Speaking"] if test else SKILL_ROTATION[idx % 6],
                    type="MockTest" if test else "Regular", exam="Ca 1 (đề đầy đủ 4 kỹ năng)" if test else None,
                    starts=begin, ends=finish))
        day += timedelta(days=1)
    return ([live] if live else []) + regular


# ------------------------------------------------------------------ file / hash helpers

def ascii_text(text):
    text = text.replace("Đ", "D").replace("đ", "d")
    text = text.replace("–", "-").replace("—", "-").replace("·", "|")
    return unicodedata.normalize("NFKD", text).encode("ascii", "ignore").decode()


def make_pdf(title, lines):
    def esc(s): return ascii_text(s).replace("\\", "\\\\").replace("(", "\\(").replace(")", "\\)")
    body = "BT /F1 14 Tf 50 780 Td (" + esc(title) + ") Tj /F1 11 Tf " + " ".join(
        f"0 -22 Td ({esc(line)}) Tj" for line in lines) + " ET"
    stream = body.encode("ascii")
    objects = [b"<< /Type /Catalog /Pages 2 0 R >>", b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
               b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R "
               b"/Resources << /Font << /F1 5 0 R >> >> >>",
               b"<< /Length " + str(len(stream)).encode() + b" >>\nstream\n" + stream + b"\nendstream",
               b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"]
    out, offsets = bytearray(b"%PDF-1.4\n"), []
    for number, obj in enumerate(objects, 1):
        offsets.append(len(out))
        out += f"{number} 0 obj\n".encode() + obj + b"\nendobj\n"
    xref = len(out)
    out += f"xref\n0 {len(objects) + 1}\n0000000000 65535 f \n".encode()
    for offset in offsets:
        out += f"{offset:010d} 00000 n \n".encode()
    out += f"trailer\n<< /Size {len(objects) + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n".encode()
    return bytes(out)


def is_loopback(url):
    parsed = urlparse(url)
    return parsed.scheme in ("http", "https") and parsed.hostname in ("localhost", "127.0.0.1", "::1") \
        and not parsed.username and not parsed.fragment


# ------------------------------------------------------------------ environment

def load_env():
    if not ENV_FILE.exists():
        raise Stop("Missing .env.demo. Run: python scripts/demo/seed_demo.py up")
    values = {}
    for line in ENV_FILE.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line and not line.startswith("#") and "=" in line:
            key, _, value = line.partition("=")
            values[key.strip()] = value.strip()
    if values.get("DEMO_STACK") != MARKER:
        raise Stop(".env.demo is not the isolated demo environment (missing DEMO_STACK marker).")
    if values.get("ACADEMIC_RESOLVE_MISSED", "false").lower() == "true":
        raise Stop("ResolveMissed must stay disabled in the demo environment.")
    return values


def env_lines():
    token = secrets.token_hex
    return [
        "# Isolated demo stack ONLY. Generated locally; ignored by git. Never reuse for VPS/production.",
        f"DEMO_STACK={MARKER}",
        f"POSTGRES_PASSWORD={token(16)}", f"JWT_SIGNING_KEY={token(32)}",
        f"MINIO_ROOT_USER=demoroot{token(4)}", f"MINIO_ROOT_PASSWORD={token(16)}",
        f"MEDIA_STORAGE_ACCESS_KEY=demoapp{token(4)}", f"MEDIA_STORAGE_SECRET_KEY={token(16)}",
        "MEDIA_STORAGE_BUCKET=english-center-demo-media", "MEDIA_STORAGE_REGION=us-east-1",
        "MEDIA_STORAGE_PUBLIC_ENDPOINT=http://localhost:19000",
        "MEDIA_STORAGE_CORS_ORIGINS=http://localhost:18088,http://127.0.0.1:18088",
        f"BOOTSTRAP_ADMIN_EMAIL=admin@{MAIL}", "BOOTSTRAP_ADMIN_NAME=LMS Administrator",
        f"BOOTSTRAP_ADMIN_PASSWORD={secrets.token_urlsafe(14)}",
        f"DEMO_ACTOR_PASSWORD={secrets.token_urlsafe(12)}",
        "API_PORT=18080", "WEB_PORT=18088", "MINIO_PORT=19000", "MINIO_CONSOLE_PORT=19001",
    ]


def ensure_env():
    """Create .env.demo with fresh random secrets only if absent; an existing file is never rewritten."""
    if ENV_FILE.exists():
        return False
    ENV_FILE.write_text("\n".join(env_lines()) + "\n", encoding="utf-8")
    return True


def compose(*args, web=True, capture=False, stdin=None):
    files = ["-f", "compose.minio.yaml", "-f", "scripts/demo/compose.demo.yaml"]
    if web:
        files += ["-f", "compose.web.yaml"]
    command = ["docker", "compose", "-p", PROJECT, "--env-file", str(ENV_FILE), *files, *args]
    # A surrounding shell may export another stack's credentials/ports; .env.demo is authoritative.
    demo_keys = load_env().keys() | {"IMAGE_TAG", "ACADEMIC_RESOLVE_MISSED", "POSTGRES_PASSWORD", "JWT_SIGNING_KEY",
                                   "MINIO_ROOT_USER", "MINIO_ROOT_PASSWORD", "MEDIA_STORAGE_ACCESS_KEY",
                                   "MEDIA_STORAGE_SECRET_KEY", "MEDIA_STORAGE_BUCKET", "MEDIA_STORAGE_REGION",
                                   "MEDIA_STORAGE_PUBLIC_ENDPOINT", "MEDIA_STORAGE_CORS_ORIGINS", "BOOTSTRAP_ADMIN_EMAIL",
                                   "BOOTSTRAP_ADMIN_PASSWORD", "BOOTSTRAP_ADMIN_NAME", "API_PORT", "WEB_PORT",
                                   "MINIO_PORT", "MINIO_CONSOLE_PORT"}
    env = {key: value for key, value in os.environ.items() if key.upper() not in demo_keys}
    env["IMAGE_TAG"] = "demo-local"
    try:
        return subprocess.run(command, cwd=ROOT, input=stdin, encoding="utf-8", capture_output=capture, check=False, env=env)
    except FileNotFoundError:
        raise Stop("Docker CLI not found.")


def require_docker():
    try:
        if os.environ.get("DOCKER_HOST") or os.environ.get("DOCKER_CONTEXT"):
            raise Stop("Unset DOCKER_HOST and DOCKER_CONTEXT before using the isolated demo.")
        context = subprocess.run(["docker", "context", "inspect", "--format", "{{.Endpoints.docker.Host}}"],
                                 capture_output=True, text=True, timeout=10)
        if context.returncode or not context.stdout.strip().startswith(("npipe:////./pipe/", "unix:///")):
            raise Stop("The demo requires a local Docker socket, not a remote context.")
        done = subprocess.run(["docker", "info", "--format", "{{.ServerVersion}}"], capture_output=True, text=True,
                              timeout=30)
    except (FileNotFoundError, subprocess.TimeoutExpired):
        done = None
    if done is None or done.returncode != 0:
        raise Stop("Docker daemon is not reachable. Start Docker Desktop yourself, then retry.")


# ------------------------------------------------------------------ HTTP

class Actor:
    def __init__(self, email, password, role):
        self.email, self.password, self.role = email, password, role


class Resp:
    def __init__(self, status, data):
        self.status, self.data = status, data

    def title(self):
        return self.data.get("title", "") if isinstance(self.data, dict) else ""


class Api:
    def __init__(self, base):
        if not is_loopback(base):
            raise Stop("Refusing non-loopback API URL; the demo seed only talks to a local isolated stack.")
        parsed = urlparse(base)
        self.host, self.port = parsed.hostname, parsed.port or (443 if parsed.scheme == "https" else 80)
        self.tokens, self.last_login, self.logins = {}, 0.0, 0

    def raw(self, method, path, body=None, token=None, headers=None, data=None):
        send = {"Accept": "application/json", **(headers or {})}
        payload = data
        if body is not None:
            payload = json.dumps(body).encode()
            send["Content-Type"] = "application/json"
        if token:
            send["Authorization"] = f"Bearer {token}"
        connection = http.client.HTTPConnection(self.host, self.port, timeout=60)
        try:
            connection.request(method, path, payload, send)
            response = connection.getresponse()
            text = response.read()
            try:
                parsed = json.loads(text) if text else None
            except ValueError:
                parsed = None
            return Resp(response.status, parsed)
        finally:
            connection.close()

    def login(self, actor):
        cached = self.tokens.get(actor.email)
        if cached and cached[1] > time.time() + 60:
            return cached[0]
        for _ in range(5):
            wait = LOGIN_GAP - (time.time() - self.last_login)
            if wait > 0:
                time.sleep(wait)
            self.last_login = time.time()
            self.logins += 1
            reply = self.raw("POST", "/api/identity/auth/login", dict(email=actor.email, password=actor.password))
            if reply.status == 429:
                time.sleep(30)
                continue
            if reply.status != 200:
                raise Stop(f"Login failed for {actor.email.split('@')[0]} (HTTP {reply.status}).")
            self.tokens[actor.email] = (reply.data["accessToken"], time.time() + 14 * 60)
            return reply.data["accessToken"]
        raise Stop("Login is rate limited; retry later.")

    def try_login(self, actor):
        try:
            self.login(actor)
            return True
        except Stop as failure:
            if "HTTP 401" in str(failure) or "HTTP 403" in str(failure):
                return False
            raise

    def call(self, actor, method, path, body=None, ok=(200, 201, 202, 204), soft=()):
        for attempt in range(3):
            reply = self.raw(method, path, body, token=self.login(actor) if actor else None)
            if reply.status == 401 and actor and attempt == 0:
                self.tokens.pop(actor.email, None)
                continue
            if reply.status == 429 and attempt < 2:
                time.sleep(20)
                continue
            break
        if reply.status in ok or reply.status in soft:
            return reply
        raise Stop(f"{method} {path.split('?')[0]} -> HTTP {reply.status} {reply.title()[:120]}")

    def upload(self, url, headers, data):
        parsed = urlparse(url)
        if not is_loopback(url):
            raise Stop("Refusing non-loopback storage URL.")
        connection = http.client.HTTPConnection(parsed.hostname, parsed.port or 80, timeout=120)
        try:
            target = parsed.path + (f"?{parsed.query}" if parsed.query else "")
            connection.request("PUT", target, data, headers)
            status = connection.getresponse().status
        finally:
            connection.close()
        if status not in (200, 201, 204):
            raise Stop(f"Storage upload rejected (HTTP {status}).")


# ------------------------------------------------------------------ seeding

class Seeder:
    def __init__(self, env, base, now=None):
        self.env, self.api = env, Api(base)
        self.now = now or datetime.now(timezone.utc)
        self.today = vn_today(self.now)
        pw = env["DEMO_ACTOR_PASSWORD"]
        self.admin = Actor(env["BOOTSTRAP_ADMIN_EMAIL"], env["BOOTSTRAP_ADMIN_PASSWORD"], "Admin")
        self.staff = [Actor(staff_email(n), pw, "Staff") for n in range(1, len(STAFF) + 1)]
        self.teachers = [Actor(teacher_email(n), pw, "Teacher") for n in range(1, len(TEACHERS) + 1)]
        self.students = [Actor(student_email(i), pw, "Student") for i in range(N_STUDENTS)]
        self.stats, self.gaps = {}, []
        self._turn = 0
        self._teacher_ids = self._classes = self._courses = None
        self.load_cache()

    # --- bookkeeping
    def count(self, key, n=1): self.stats[key] = self.stats.get(key, 0) + n
    def gap(self, text):
        if text not in self.gaps:
            self.gaps.append(text)

    def log(self, text): print(text, flush=True)

    def manager(self):
        self._turn += 1
        return self.staff[self._turn % len(self.staff)]

    def load_cache(self):
        if STATE_FILE.exists():
            try:
                self.cache = json.loads(STATE_FILE.read_text(encoding="utf-8"))
            except ValueError:
                self.cache = {}
        else:
            self.cache = {}

    def save_cache(self):
        STATE_FILE.write_text(json.dumps(self.cache, indent=1), encoding="utf-8")

    # --- preflight
    def preflight(self):
        if self.api.raw("GET", "/health").status != 200:
            raise Stop("API /health is not reachable. Start the stack: python scripts/demo/seed_demo.py up")
        me = self.api.call(self.admin, "GET", "/api/identity/me").data
        if self.cache.get("admin_id") != me["id"]:  # fresh stack: drop stale id cache
            self.cache = {"admin_id": me["id"], "ids": {}}
            self.save_cache()
        self.cache.setdefault("ids", {})

    # --- lookups
    def courses(self):
        if self._courses is None:
            data = self.api.call(self.manager(), "GET", "/api/academic/courses").data
            self._courses = {c["code"]: c["id"] for c in data}
        return self._courses

    def teacher_ids(self, refresh=False):
        if self._teacher_ids is None or refresh:
            data = self.api.call(self.manager(), "GET", "/api/academic/teachers").data
            self._teacher_ids = {t["email"].lower(): t["id"] for t in data}
        return self._teacher_ids

    def teacher_id(self, n):
        found = self.teacher_ids().get(teacher_email(n))
        if not found:
            found = self.teacher_ids(True).get(teacher_email(n))
        if not found:
            raise Stop(f"Teacher {n} is missing in Academic; run phase accounts first.")
        return found

    def student_id(self, i):
        known = self.cache["ids"].get(student_email(i))
        if known:
            return known
        me = self.api.call(self.students[i], "GET", "/api/identity/me").data
        self.cache["ids"][student_email(i)] = me["id"]
        self.save_cache()
        return me["id"]

    def classes(self, refresh=False):
        if self._classes is None or refresh:
            query = urlencode(dict(page=1, pageSize=100))
            data = self.api.call(self.manager(), "GET", f"/api/academic/classes?{query}").data
            if data["total"] > len(data["items"]):
                raise Stop("Too many demo-like classes to inspect safely.")
            self._classes = {c["code"]: c for c in data["items"]}
        return self._classes

    def klass(self, cls):
        found = self.classes().get(cls["code"])
        if not found:
            raise Stop(f"Class {cls['code']} is missing; run phase classes first.")
        return found

    def memberships(self, cls):
        return self.api.call(self.manager(), "GET", f"/api/enrollment/memberships?classId={self.klass(cls)['id']}").data

    def active_members(self, cls):
        by_student = {m["studentId"]: m for m in self.memberships(cls) if m["isActive"]}
        return [i for i in members(cls) if self.student_id(i) in by_student]

    def class_sessions(self, cls):
        item = self.klass(cls)
        start, end = date.fromisoformat(item["startDate"]), date.fromisoformat(item["endDate"])
        if (end - start).days > 91:
            raise Stop(f"{cls['code']} spans more than 92 days; session listing would be rejected.")
        query = urlencode(dict(classId=item["id"], fromUtc=iso(at(start, 0)), toUtc=iso(at(end + timedelta(days=1), 0))))
        return self.api.call(self.manager(), "GET", f"/api/academic/sessions?{query}").data

    def teacher_actor(self, cls): return self.teachers[cls["teacher"] - 1]

    # --- phases
    def phase_accounts(self):
        missing = [s for s in self.staff if not self.api.try_login(s)]
        if missing:
            self.provision_staff(missing)
        creator = self.staff[0]
        teachers = self.teacher_ids(True)
        for n, name in enumerate(TEACHERS, 1):
            if teacher_email(n) in teachers:
                continue
            self.api.call(creator, "POST", "/api/identity/users", dict(
                email=teacher_email(n), fullName=name, password=self.env["DEMO_ACTOR_PASSWORD"], role="Teacher",
                phone=f"0902{n:06d}"))
            self.count("teachers_created")
        self.teacher_ids(True)
        for i in range(N_STUDENTS):
            if self.cache["ids"].get(student_email(i)):
                continue
            reply = self.api.call(creator, "POST", "/api/identity/users", dict(
                email=student_email(i), fullName=student_name(i), password=self.env["DEMO_ACTOR_PASSWORD"],
                role="Student", phone=f"0903{i:06d}"), soft=(409,))
            if reply.status == 201:
                self.cache["ids"][student_email(i)] = reply.data["id"]
                self.count("students_created")
            else:
                self.student_id(i)  # exists already: resolve its id through its own login
            self.save_cache()
        self.log(f"accounts: staff={len(STAFF)} teachers={len(TEACHERS)} students={N_STUDENTS}")

    def provision_staff(self, actors):
        for actor in actors:
            n = int(actor.email.split("@")[0][5:])
            reply = self.api.call(self.admin, "POST", "/api/identity/users/staff", dict(
                email=actor.email, fullName=STAFF[n - 1], password=self.env["DEMO_ACTOR_PASSWORD"],
                phone=f"0901{n:06d}"), soft=(409,))
            if reply.status == 201:
                self.count("staff_provisioned")
            self.api.login(actor)

    def phase_classes(self):
        for cls in CLASSES:
            if cls["code"] in self.classes():
                continue
            self.api.call(self.manager(), "POST", "/api/academic/classes", dict(
                code=cls["code"], name=cls["name"], courseId=self.courses()[cls["course"]], classType=cls["type"],
                startDate=str(self.today + timedelta(days=cls["start"])), endDate=str(self.today + timedelta(days=cls["end"])),
                maxStudents=cls["max"], tuitionFee=cls["fee"], teacherId=self.teacher_id(cls["teacher"])))
            self.count("classes_created")
        self.classes(True)
        self.log(f"classes: {len(self.classes())}")

    def phase_sessions(self):
        for cls in CLASSES:
            item = self.klass(cls)
            start, end = date.fromisoformat(item["startDate"]), date.fromisoformat(item["endDate"])
            existing = self.class_sessions(cls)
            seen_units = {s["unit"] for s in existing}
            seen_starts = {parse_ts(s["startsAtUtc"]) for s in existing}
            for plan in session_plan(cls, start, end, self.today, self.now):
                if (plan["unit"].startswith("Live ") and plan["unit"] in seen_units) or plan["starts"] in seen_starts:
                    continue
                body = dict(classId=item["id"], startsAtUtc=iso(plan["starts"]), endsAtUtc=iso(plan["ends"]),
                            unit=plan["unit"], lesson=plan["lesson"], skills=plan["skills"], sessionType=plan["type"])
                if plan["exam"]:
                    body["examSlot"] = plan["exam"]
                reply = self.api.call(self.manager(), "POST", "/api/academic/sessions", body, soft=(400, 409))
                if reply.status == 201:
                    self.count("sessions_created")
                else:
                    self.count("sessions_skipped")
                    self.gap(f"Some sessions were rejected by scheduling rules (HTTP {reply.status}); first: {plan['unit']}")
        self.log(f"sessions: created={self.stats.get('sessions_created', 0)} skipped={self.stats.get('sessions_skipped', 0)}")

    def enroll(self, cls, i, class_id):
        body = dict(studentId=self.student_id(i), classId=class_id,
                    maxAbsences=0 if warning_example(cls, i) else 3, maxLates=4, maxSkippedAssignments=2)
        reply = self.api.call(self.manager(), "POST", "/api/enrollment/enrollments", body, soft=(400, 409))
        if reply.status == 400:  # in-memory event bus: re-publish the missing snapshot once, then retry
            self.api.call(self.admin, "POST", "/api/identity/users/republish", dict(role="Student"))
            self.api.call(self.manager(), "POST", f"/api/academic/classes/{class_id}/replay")
            reply = self.api.call(self.manager(), "POST", "/api/enrollment/enrollments", body, soft=(409,))
        if reply.status == 409:
            self.gap(f"Enrollment rejected with 409 (full or already enrolled) in {cls['code']}")
        else:
            self.count("enrollments_created")

    def phase_enrollment(self):
        for cls in CLASSES:
            if not cls["n"]:
                continue
            class_id = self.klass(cls)["id"]
            have = {m["studentId"] for m in self.memberships(cls)}
            for i in members(cls):
                if self.student_id(i) not in have:
                    self.enroll(cls, i, class_id)
            for m in self.memberships(cls):
                if m["isActive"] and m["studentId"] in {self.student_id(i) for i in revoked(cls)}:
                    self.api.call(self.manager(), "PATCH", f"/api/enrollment/memberships/{m['id']}/status", dict(isActive=False))
                    self.count("memberships_revoked")
        self.log(f"enrollment: created={self.stats.get('enrollments_created', 0)}")

    def phase_finance(self):
        for cls in CLASSES:
            if not cls["n"]:
                continue
            for m in self.memberships(cls):
                path = f"/api/finance/invoices/by-enrollment/{m['id']}"
                invoice = self.api.call(self.manager(), "GET", path, soft=(404,))
                if invoice.status == 404:
                    self.api.call(self.manager(), "POST", f"/api/enrollment/enrollments/{m['id']}/replay")
                    invoice = self.api.call(self.manager(), "GET", path, soft=(404,))
                if invoice.status == 404:
                    self.gap(f"No invoice generated for an enrollment in {cls['code']}")
                    continue
                if invoice.data["status"] == "Unpaid" and m["isActive"] and chance(0.65, "pay", m["id"]):
                    self.api.call(self.manager(), "POST", f"/api/finance/invoices/{invoice.data['id']}/pay")
                    self.count("invoices_paid")
        self.log(f"finance: paid_now={self.stats.get('invoices_paid', 0)}")

    def phase_placement(self):
        for i, seq, offset, mode, status in PLACEMENTS:
            sid = self.student_id(i)
            name = f"Kiểm tra đầu vào IELTS {i:02d}-{seq}"
            listing = self.api.call(self.manager(), "GET", f"/api/enrollment/placement-tests?studentId={sid}&page=1&pageSize=100").data
            if any(t["testName"] == name for t in listing["items"]):
                continue
            when = (self.now + timedelta(days=offset)).replace(hour=2, minute=0, second=0, microsecond=0)
            future = (self.now + timedelta(days=3)).replace(hour=2, minute=0, second=0, microsecond=0)
            teacher = self.teacher_id(1 + i % len(TEACHERS))
            location = "Cơ sở chính - Phòng 201" if mode == "Offline" else "Google Meet (link gửi qua email)"
            created = self.api.call(self.manager(), "POST", "/api/enrollment/placement-tests", dict(
                studentId=sid, location=location, mode=mode, scheduledAt=iso(future if status != "Pending" else when),
                testName=name, teacherId=teacher)).data
            if status != "Pending":  # create requires a future time; the past/cancelled state is a supported update
                self.api.call(self.manager(), "PUT", f"/api/enrollment/placement-tests/{created['id']}", dict(
                    location=location, mode=mode, scheduledAt=iso(when), testName=name, status=status, teacherId=teacher))
            self.count("placement_created")
        self.log(f"placement: created={self.stats.get('placement_created', 0)}")

    def phase_attendance(self):
        statuses = [("Present", 0.62), ("Late", 0.12), ("LateOver20Min", 0.05), ("AbsentExcused", 0.08),
                    ("AbsentUnexcused", 0.08), ("LeftEarly", 0.05)]
        for cls in CLASSES:
            if not cls["n"]:
                continue
            item, teacher = self.klass(cls), self.teacher_actor(cls)
            roster = self.active_members(cls)
            for session in self.class_sessions(cls):
                start, end = parse_ts(session["startsAtUtc"]), parse_ts(session["endsAtUtc"])
                if session.get("isCancelled") or not (start <= self.now <= end + timedelta(hours=23, minutes=45)):
                    continue
                query = urlencode(dict(classId=item["id"], sessionId=session["id"], page=1, pageSize=100))
                done = {row["studentId"] for row in self.api.call(self.manager(), "GET", f"/api/academic/attendance?{query}").data["items"]}
                for i in roster:
                    sid = self.student_id(i)
                    if sid in done:
                        continue
                    roll, status = random.Random(f"att|{session['id']}|{i}").random(), "Present"
                    for name, share in statuses:
                        roll -= share
                        if roll < 0:
                            status = name
                            break
                    sample = warning_example(cls, i) and session["unit"].startswith("Live ")
                    if sample:
                        status = "AbsentUnexcused"
                    path = f"/api/academic/sessions/{session['id']}/attendance/{sid}"
                    row = self.api.call(teacher, "PUT", path, dict(status=status, expectedRevision=None)).data
                    self.count("attendance_written")
                    if not sample and chance(0.18, "fix", session["id"], i):  # teacher corrects a mark -> revision history
                        fixed = pick([s for s, _ in statuses if s != status], "fixto", session["id"], i)
                        self.api.call(teacher, "PUT", path, dict(status=fixed, expectedRevision=row["revision"]))
                        self.count("attendance_corrected")
        self.log(f"attendance: written={self.stats.get('attendance_written', 0)}")
        self.gap("Attendance is writable only until 24h after a session ends: older history cannot be backfilled; "
                 "re-run on later days to accumulate it.")

    def phase_practice(self):
        for cls in CLASSES:
            item, teacher = self.klass(cls), self.teacher_actor(cls)
            topics = TOPICS[cls["course"]]
            existing = {p["title"] for p in self.api.call(self.manager(), "GET",
                        f"/api/academic/practice?classId={item['id']}&page=1&pageSize=100").data["items"]}
            plans = [(teacher, f"Luyện tập: {topics[0]}",
                      f"Bài luyện {topics[0]}.\n1. Đọc kỹ đề, gạch từ khóa.\n2. Làm trong 20 phút.\n3. Tự đối chiếu đáp án "
                      f"và ghi lại 3 lỗi sai thường gặp.\nNộp lại lỗi sai vào buổi học tới."),
                     (teacher, f"Gợi ý chiến lược: {topics[1]}",
                      f"Chiến lược cho {topics[1]}: quản lý thời gian, nhận diện bẫy, và cách kiểm tra lại đáp án cuối giờ.")]
            if cls["k"] == 0:
                plans.append((self.manager(), "Quy định làm bài luyện tập (giáo vụ)",
                              "Bài luyện tập là nội dung văn bản tham khảo, không chấm điểm. Liên hệ giáo viên nếu cần giải thích."))
            for who, title, content in plans:
                if title in existing:
                    continue
                self.api.call(who, "POST", "/api/academic/practice", dict(classId=item["id"], title=title, content=content))
                self.count("practice_created")
        self.log(f"practice: created={self.stats.get('practice_created', 0)}")

    # --- coursework
    def upload_pdf(self, actor, name, lines, purpose="submission"):
        data = make_pdf(name, lines)
        ticket = self.api.call(actor, "POST", "/api/media-notification/files/presign", dict(
            fileName=f"{name}.pdf", contentType="application/pdf", sizeBytes=len(data), purpose=purpose)).data
        self.api.upload(ticket["url"], ticket["requiredHeaders"], data)
        self.api.call(actor, "POST", f"/api/media-notification/files/{ticket['fileId']}/confirm")
        return ticket["fileId"]

    def pick_session(self, sessions, which):
        ordered = sorted(sessions, key=lambda s: s["startsAtUtc"])
        if not ordered:
            return None
        upcoming = [s for s in ordered if parse_ts(s["startsAtUtc"]) > self.now]
        if which == "past":
            return ordered[0]["id"]
        if which == "live":
            live = [s for s in ordered if s["unit"].startswith("Live ")]
            return live[-1]["id"] if live else None
        pool = upcoming or ordered
        return pool[min(0 if which == "next" else 2, len(pool) - 1)]["id"]

    def phase_worksheets(self):
        from textwrap import wrap
        from worksheets import WORKSHEETS

        for cls in CLASSES:
            if not cls["n"]:
                continue
            item, teacher = self.klass(cls), self.teacher_actor(cls)
            session = self.pick_session(self.class_sessions(cls), "next")
            if session is None:
                raise Stop(f"No worksheet session for {cls['code']}.")
            classes = (0, 1, 2, 7) if cls["course"] == "IELTS-FOUNDATION" else (
                (2, 3, 4, 5) if cls["course"] == "IELTS-INTERMEDIATE" else (3, 4, 5, 6))
            existing = self.api.call(teacher, "GET", f"/api/academic/assignments?classId={item['id']}").data
            known = {a["title"] for a in existing}
            for index in classes:
                key, title, content = WORKSHEETS[index]
                if title in known:
                    continue
                lines = [line for paragraph in content.splitlines() for line in (wrap(paragraph, width=78) or [""])]
                if len(lines) > 33:
                    raise Stop(f"Worksheet won't fit one PDF page: {key}")
                prompt = self.upload_pdf(teacher, key, lines, "material")
                opens = max(self.now - timedelta(minutes=5), at(date.fromisoformat(item["startDate"]), 0))
                self.api.call(teacher, "POST", "/api/academic/assignments", dict(
                    classId=item["id"], title=title, description=content, kind="Homework", sessionId=session,
                    opensAtUtc=iso(opens), closesAtUtc=iso(max(opens, self.now) + timedelta(days=28)),
                    publish=True, promptFileId=prompt))
                self.count("worksheets_created")
        self.log(f"worksheets: created={self.stats.get('worksheets_created', 0)}")

    def phase_coursework(self):
        day = self.today
        for cls in CLASSES:
            if not cls["n"]:
                continue  # the finished class has no students to work with
            item, teacher = self.klass(cls), self.teacher_actor(cls)
            topics = TOPICS[cls["course"]]
            sessions = self.class_sessions(cls)
            code = cls["code"]
            plans = [
                dict(title=f"[{code}] Bài tập tuần: {topics[0]}", kind="Homework", session="next", publish=True,
                     opens=at(day - timedelta(days=3), 8), closes=at(day + timedelta(days=4), 23, 59), submit=0.75, grade=0.65),
                dict(title=f"[{code}] Làm trên lớp: {topics[0]} (Live)", kind="InClass", session="live", publish=True,
                     opens=self.now - timedelta(hours=3), closes=at(day + timedelta(days=1), 23, 59), submit=0.45, grade=0.4),
                dict(title=f"[{code}] Bài tập đã đóng: {topics[1]}", kind="Homework", session="past", publish=True,
                     opens=at(day - timedelta(days=14), 8), closes=at(day - timedelta(days=7), 23, 59), submit=0, grade=0),
                dict(title=f"[{code}] Bài tập sắp mở: {topics[2]}", kind="Homework", session="later", publish=True,
                     opens=at(day + timedelta(days=2), 8), closes=at(day + timedelta(days=9), 23, 59), submit=0, grade=0),
                dict(title=f"[{code}] Bản nháp: {topics[3]}", kind="Homework", session=None, publish=False,
                     opens=at(day + timedelta(days=5), 8), closes=at(day + timedelta(days=12), 23, 59), submit=0, grade=0),
            ]
            if cls["start"] > 0:  # not started yet: only an upcoming and a draft assignment make sense
                plans = plans[3:]
            else:
                plans.append(dict(title=f"[{code}] Đề thi PDF: {topics[0]}", kind="Exam", session="next", publish=True,
                                  opens=self.now - timedelta(hours=1), closes=self.now + timedelta(hours=36), submit=0.55, grade=0.65))
            material_title = f"[{code}] Tài liệu lớp: {topics[0]}"
            materials = self.api.call(teacher, "GET", f"/api/academic/materials?classId={item['id']}").data
            if not any(m["title"] == material_title for m in materials):
                file_id = self.upload_pdf(teacher, f"tai-lieu-{code.lower()}", [topics[0], "Review the vocabulary and write five sentences."], "material")
                self.api.call(teacher, "POST", "/api/academic/materials", dict(classId=item["id"], title=material_title, fileId=file_id))
                self.count("materials_created")
            existing = {a["title"]: a for a in self.api.call(self.manager(), "GET", f"/api/academic/assignments?classId={item['id']}").data}
            roster = self.active_members(cls)
            for plan in plans:
                found = existing.get(plan["title"])
                if found is None:
                    session = self.pick_session(sessions, plan["session"]) if plan["session"] else None
                    if plan["publish"] and session is None:
                        continue  # e.g. no live session for classes that have not started yet
                    body = dict(classId=item["id"], title=plan["title"],
                                description=f"Hoàn thành bài về {topics[0].split(':')[0]} và nộp file PDF.",
                                opensAtUtc=iso(plan["opens"]), closesAtUtc=iso(plan["closes"]), kind=plan["kind"],
                                sessionId=session, publish=plan["publish"])
                    if plan["kind"] == "Exam":
                        body["promptFileId"] = self.upload_pdf(teacher, f"de-thi-{code.lower()}",
                                                              ["Write a short paragraph and give two examples."], "exam")
                    found = self.api.call(teacher, "POST", "/api/academic/assignments", body).data
                    self.count("assignments_created")
                if plan["submit"] and found["status"] == "Open":
                    self.submit_and_grade(cls, teacher, found, roster, plan)
        self.log(f"coursework: assignments={self.stats.get('assignments_created', 0)} "
                 f"submissions={self.stats.get('submissions_created', 0)} grades={self.stats.get('grades_written', 0)}")
        self.gap("Closed assignments stay empty: the API only accepts submissions while an assignment is open.")
        self.gap("Missed/Exempted outcomes need Academic:Coursework:ResolveMissed, intentionally disabled here.")

    def submit_and_grade(self, cls, teacher, assignment, roster, plan):
        subs = {s["studentId"]: s for s in self.api.call(
            self.manager(), "GET", f"/api/academic/assignments/{assignment['id']}/submissions").data}
        for i in roster:
            sid = self.student_id(i)
            if sid in subs or not chance(plan["submit"], "submit", assignment["id"], i):
                continue
            student = self.students[i]
            file_id = self.upload_pdf(student, f"bai-nop-hv{i:02d}-{cls['code'].lower()}",
                                      [f"Hoc vien: {student_name(i)}", f"Lop: {cls['code']}", assignment["title"][:80]])
            subs[sid] = self.api.call(student, "PUT", f"/api/academic/assignments/{assignment['id']}/submissions",
                                      dict(fileIds=[file_id], expectedRevision=None)).data
            self.count("submissions_created")
        for i in roster:
            sub = subs.get(self.student_id(i))
            if not sub or sub.get("gradeVersion", 0) or not chance(plan["grade"], "grade", assignment["id"], i):
                continue
            score = pick([5.0, 5.5, 6.0, 6.5, 7.0, 7.5, 8.0, 8.5, 9.0], "score", assignment["id"], i)
            note = "Bài làm tốt, cần thêm ví dụ cụ thể." if score >= 7 else "Cần luyện thêm cấu trúc và từ vựng chủ đề."
            graded = self.api.call(teacher, "PUT", f"/api/academic/submissions/{sub['id']}/grade", dict(
                score=score, feedback=note, expectedGradeVersion=0, expectedRevision=sub["revision"])).data
            self.count("grades_written")
            if chance(0.2, "regrade", assignment["id"], i):  # grade history
                self.api.call(teacher, "PUT", f"/api/academic/submissions/{sub['id']}/grade", dict(
                    score=min(10, score + 0.5), feedback="Sau phúc khảo: điều chỉnh điểm.", expectedGradeVersion=1,
                    expectedRevision=graded["revision"]))

    # --- feed / feedback / faq / notifications
    def phase_feed(self):
        for cls in CLASSES:
            if not cls["n"]:
                continue
            item, teacher, code = self.klass(cls), self.teacher_actor(cls), cls["code"]
            roster = self.active_members(cls)
            if len(roster) < 3:
                continue
            skill = TOPICS[cls["course"]][0].split(":")[0]
            base = f"/api/media-notification/classes/{item['id']}/posts"
            listing = self.api.call(self.manager(), "GET", f"{base}?page=1&pageSize=100").data["items"]
            by_content = {p["content"]: p for p in listing}
            texts = dict(
                welcome=(teacher, f"Chào mừng các bạn đến với lớp {code}! Lịch học và tài liệu được cập nhật tại đây. Nhớ làm bài tập trước mỗi buổi nhé."),
                notice=(self.manager(), f"Thông báo từ giáo vụ ({code}): vui lòng kiểm tra mục Hóa đơn và liên hệ giáo vụ nếu cần hỗ trợ."),
                ask=(self.students[roster[0]], f"Cô/thầy ơi, buổi tới mình cần chuẩn bị gì cho phần {skill} ạ? ({code})"),
                share=(self.students[roster[2]], f"Mình chia sẻ mẹo làm {skill}: đọc câu hỏi trước, gạch từ khóa rồi mới nghe/đọc. ({code})"))
            if cls["k"] in (0, 3):
                texts["spam"] = (self.students[roster[1]], f"Ai bán sách IELTS cũ không ạ? Nhắn riêng mình nhé. ({code})")
            posts = {}
            for key, (who, content) in texts.items():
                posts[key] = by_content.get(content) or self.api.call(who, "POST", base, dict(content=content)).data
                if content not in by_content:
                    self.count("posts_created")
            if "spam" in posts and not posts["spam"]["isHidden"]:
                self.api.call(teacher, "PUT", f"/api/media-notification/posts/{posts['spam']['id']}/hidden", dict(hidden=True))
                self.count("posts_hidden")
            for key, likers in (("welcome", roster[:5]), ("ask", roster[1:3]), ("share", roster[3:6])):
                if posts[key]["likeCount"] < len(likers):
                    for i in likers:
                        self.api.call(self.students[i], "PUT", f"/api/media-notification/posts/{posts[key]['id']}/like")
                    self.count("likes_set", len(likers))
            comments = {}
            plans = [("ask", teacher, f"Cảm ơn bạn đã hỏi! Xem lại phần {skill} ở mục Luyện tập và làm bài tập tuần trước nhé. ({code})"),
                     ("welcome", self.students[roster[1]], f"Em cảm ơn cô/thầy ạ, em đã vào xem lịch học. ({code})"),
                     ("welcome", self.students[roster[2]], f"Lớp mình có nhóm học chung không ạ? ({code})")]
            if cls["k"] == 3:
                plans.append(("share", self.students[roster[1]], f"Bình luận quảng cáo không liên quan đến bài học. ({code})"))
            for key, who, content in plans:
                post_id = posts[key]["id"]
                if post_id not in comments:
                    comments[post_id] = {c["content"]: c for c in self.api.call(
                        self.manager(), "GET", f"/api/media-notification/posts/{post_id}/comments?page=1&pageSize=100").data["items"]}
                found = comments[post_id].get(content)
                if found is None:
                    found = self.api.call(who, "POST", f"/api/media-notification/posts/{post_id}/comments", dict(content=content)).data
                    self.count("comments_created")
                if content.startswith("Bình luận quảng cáo") and not found["isHidden"]:
                    self.api.call(teacher, "PUT", f"/api/media-notification/comments/{found['id']}/hidden", dict(hidden=True))
        self.log(f"feed: posts={self.stats.get('posts_created', 0)} comments={self.stats.get('comments_created', 0)}")

    def phase_feedback(self):
        order = ["New", "InProgress", "Resolved"]
        for i, title, content, rating, target in FEEDBACKS:
            query = urlencode(dict(search=title[:90], page=1, pageSize=100))
            found = next((f for f in self.api.call(self.manager(), "GET", f"/api/media-notification/feedback?{query}").data["items"]
                          if f["title"] == title), None)
            if found is None:
                found = self.api.call(self.students[i], "POST", "/api/media-notification/feedback",
                                      dict(title=title, content=content, rating=rating)).data
                self.count("feedback_created")
            while order.index(found["status"]) < order.index(target):
                step = order[order.index(found["status"]) + 1]
                found = self.api.call(self.manager(), "PATCH", f"/api/media-notification/feedback/{found['id']}/status",
                                      dict(status=step, expectedRevision=found["revision"])).data
                self.count("feedback_advanced")
        self.log(f"feedback: created={self.stats.get('feedback_created', 0)}")

    def phase_faq(self):
        listing = self.api.call(self.admin, "GET", "/api/media-notification/admin/faqs?page=1&pageSize=100").data["items"]
        have = {f["question"] for f in listing}
        for question, answer, published in FAQS:
            if question not in have:
                self.api.call(self.admin, "POST", "/api/media-notification/admin/faqs",
                              dict(question=question, answer=answer, isPublished=published))
                self.count("faqs_created")
        self.log(f"faq: created={self.stats.get('faqs_created', 0)}")

    def phase_notifications(self):
        # Only on a run that produced new data, so a repeat run never wipes the demo's unread state.
        if not any(self.stats.get(k) for k in ("enrollments_created", "submissions_created", "grades_written")):
            self.log("notifications: skipped (no new activity; existing read/unread state preserved)")
            return
        for i in range(N_ENROLLABLE):
            if i % 5 == 0:
                self.api.call(self.students[i], "POST", "/api/media-notification/notifications/read-all")
                self.count("notifications_read_all")
            elif i % 5 == 1:
                page = self.api.call(self.students[i], "GET", "/api/media-notification/notifications?pageSize=1").data
                if page["items"]:
                    self.api.call(self.students[i], "POST", f"/api/media-notification/notifications/{page['items'][0]['id']}/read")
        self.gap("Overdue invoices cannot be shown: invoices are only Unpaid or Paid.")
        self.log("notifications: read states applied")

    def report(self):
        self.log("--- summary (this run) ---")
        for key in sorted(self.stats):
            self.log(f"  {key}: {self.stats[key]}")
        self.log(f"  logins: {self.api.logins}")
        gaps = list(self.gaps) + [
            "Finished class has no students: enrollment is blocked after a class ends, so completed-class stats stay 0.",
            "Avatars are not seeded."]
        self.log("--- honest gaps ---")
        for text in dict.fromkeys(gaps):
            self.log(f"  - {text}")


# ------------------------------------------------------------------ verify (read-only)

def verify(env, base):
    seeder = Seeder(env, base)
    seeder.preflight()
    api, results = seeder.api, []

    def check(name, ok, detail):
        results.append(ok)
        print(f"{'PASS' if ok else 'FAIL'}  {name}: {detail}")

    classes = list(seeder.classes(True).values())
    states = {s: sum(c["status"] == s for c in classes) for s in ("Upcoming", "Ongoing", "Finished")}
    check("classes", len(classes) >= len(CLASSES) and all(states.values()), f"{len(classes)} {states}")
    check("teachers", len([t for t in seeder.teacher_ids(True) if t.startswith("gv")]) >= len(TEACHERS), "synced to Academic")
    enrolled = [seeder.memberships(c) for c in CLASSES if c["n"]]
    check("enrollments", sum(map(len, enrolled)) >= sum(c["n"] for c in CLASSES),
          f"{sum(map(len, enrolled))} total, {sum(not m['isActive'] for ms in enrolled for m in ms)} revoked")
    inv = api.call(seeder.manager(), "GET", "/api/finance/invoices?page=1&pageSize=100").data
    paid = sum(i["status"] == "Paid" for i in inv["items"])
    check("invoices", inv["total"] > 0 and 0 < paid < len(inv["items"]), f"total={inv['total']} paid(first page)={paid}")
    pt = api.call(seeder.manager(), "GET", "/api/enrollment/placement-tests?page=1&pageSize=100").data
    check("placement", {t["status"] for t in pt["items"]} >= {"Pending", "Done", "Cancelled"}, f"total={pt['total']}")
    statuses, graded, exams, materials = set(), 0, 0, 0
    for cls in CLASSES:
        class_id = seeder.klass(cls)["id"]
        materials += len(api.call(seeder.manager(), "GET", f"/api/academic/materials?classId={class_id}").data)
        for a in api.call(seeder.manager(), "GET", f"/api/academic/assignments?classId={class_id}").data:
            statuses.add(a["status"])
            exams += a["kind"] == "Exam" and bool(a.get("promptFileName"))
            if a["status"] == "Open":
                graded += sum(bool(s["gradeVersion"]) for s in api.call(
                    seeder.manager(), "GET", f"/api/academic/assignments/{a['id']}/submissions").data)
    check("coursework", statuses >= {"Draft", "Upcoming", "Open", "Closed"} and graded > 0, f"{sorted(statuses)} graded={graded}")
    check("class PDFs", materials > 0 and exams > 0, f"materials={materials} exams={exams}")
    recorded = sum(api.call(seeder.manager(), "GET", f"/api/academic/attendance/summary?classId={seeder.klass(c)['id']}").data["recordedCount"]
                   for c in CLASSES if c["n"])
    check("attendance", recorded > 0, f"{recorded} marks recorded")
    posts = sum(api.call(seeder.manager(), "GET", f"/api/media-notification/classes/{seeder.klass(c)['id']}/posts?pageSize=100").data["total"]
                for c in CLASSES if c["n"])
    check("feed", posts >= 20, f"{posts} posts")
    faq = api.raw("GET", "/api/media-notification/faqs?pageSize=100")
    check("faq (anonymous)", faq.status == 200 and faq.data["total"] >= 8, f"published={faq.data['total'] if faq.data else '?'}")
    fb = api.call(seeder.manager(), "GET", "/api/media-notification/feedback?pageSize=100").data
    check("feedback", {f["status"] for f in fb["items"]} >= {"New", "InProgress", "Resolved"}, f"total={fb['total']}")
    unread = api.call(seeder.students[2], "GET", "/api/media-notification/notifications/unread-count").data["unreadCount"]
    check("notifications", unread > 0, f"sample student unread={unread}")
    stats = api.call(seeder.students[2], "GET", "/api/academic/statistics/mine").data
    check("student statistics", stats["academic"]["currentClassCount"] > 0, f"classes={stats['academic']['currentClassCount']}")
    sample_class = CLASSES[0]
    sample_student = seeder.student_id(members(sample_class)[0])
    sample = next((m for m in seeder.memberships(sample_class) if m["studentId"] == sample_student), None)
    absence_warning = False
    if sample:
        warnings = api.call(seeder.manager(), "GET", f"/api/enrollment/warnings?enrollmentId={sample['id']}&pageSize=100").data
        absence_warning = any(w["warningType"] == "Absence" for w in warnings["items"])
    check("attendance warnings", absence_warning, f"sample enrollment in {sample_class['code']} has an absence warning")
    return 0 if all(results) else 1


# ------------------------------------------------------------------ self-check (offline)

def self_check():
    failures = []

    def ok(condition, text):
        print(f"{'ok  ' if condition else 'FAIL'} {text}")
        if not condition:
            failures.append(text)

    emails = [staff_email(n) for n in range(1, len(STAFF) + 1)] + [teacher_email(n) for n in range(1, len(TEACHERS) + 1)] + \
             [student_email(i) for i in range(N_STUDENTS)]
    ok(len(set(emails)) == len(emails), f"{len(emails)} actor emails are unique")
    names = STAFF + TEACHERS + [student_name(i) for i in range(N_STUDENTS)]
    ok(len(set(names)) == len(names), "actor display names are unique")
    ok(len({c["code"] for c in CLASSES}) == len(CLASSES) and len({c["teacher"] for c in CLASSES}) == len(CLASSES),
       "class codes unique and every class has its own teacher (no teacher overlap possible)")
    ok(all(c["n"] <= c["max"] and 0 <= c["end"] - c["start"] <= 90 for c in CLASSES), "capacity and <=92-day window hold")
    enrolled = {i for c in CLASSES for i in members(c)}
    ok(enrolled.isdisjoint(range(N_ENROLLABLE, N_STUDENTS)) and len(enrolled) == N_ENROLLABLE,
       "all 42 enrollable students get a class; applicants (42+) are never enrolled")
    ok(len({c["course"] for c in CLASSES}) == 3, "classes span all three seeded courses")
    ok(sum(c["start"] > 0 for c in CLASSES) >= 2 and any(c["end"] < 0 for c in CLASSES), "Upcoming/Ongoing/Finished all planned")
    ok(any(c["n"] == c["max"] for c in CLASSES), "one class is exactly full")
    ok(sum(1 for f in FAQS if f[2]) >= 8 and any(not f[2] for f in FAQS), "FAQs include published and draft entries")
    ok(len({f[1] for f in FEEDBACKS}) == len(FEEDBACKS) and len({f[0] for f in FEEDBACKS}) == len(FEEDBACKS),
       "one unique feedback per student (rate limit 5/hour)")
    ok({s for *_, s in PLACEMENTS} == {"Pending", "Done", "Cancelled"}, "all placement states planned")
    ok(all(len(f[1]) <= 90 for f in FEEDBACKS), "feedback titles fit the 100-char search")
    # relative scheduling invariants at every hour of a day
    base = datetime(2026, 11, 19, 0, 0, tzinfo=VN)
    bad = []
    for step in range(24):
        now = base + timedelta(hours=step, minutes=17)
        today = vn_today(now)
        for c in CLASSES:
            start, end = today + timedelta(days=c["start"]), today + timedelta(days=c["end"])
            plans = session_plan(c, start, end, today, now)
            for p in plans:
                if p["starts"] < at(start, 0) or p["ends"] > at(end + timedelta(days=1), 0) or p["ends"] <= p["starts"]:
                    bad.append(("window", c["code"], step))
            spans = sorted((p["starts"], p["ends"]) for p in plans)
            bad += [("overlap", c["code"], step) for a, b in zip(spans, spans[1:]) if b[0] < a[1]]
            live = [p for p in plans if p["unit"].startswith("Live ")]
            if live and not (live[0]["ends"] <= now <= live[0]["ends"] + timedelta(hours=23, minutes=45)):
                bad.append(("live-not-writable", c["code"], step))
            if c["end"] < 0 and live:
                bad.append(("finished-live", c["code"], step))
            if c["k"] == 0 and (len(live) != 1 or not any(warning_example(c, i) for i in members(c) if i not in revoked(c))):
                bad.append(("warning-sample-unavailable", c["code"], step))
    ok(not bad, f"session plans: windows, no overlap, live sessions attendance-writable ({bad[:2]})")
    sample = session_plan(CLASSES[0], date(2026, 10, 29), date(2026, 12, 31), date(2026, 11, 19), base + timedelta(hours=10))
    ok(any(p["type"] == "MockTest" for p in sample) and all(len(p["unit"]) <= 100 and len(p["lesson"]) <= 200 for p in sample),
       "mock tests appear and labels fit server limits")
    pdf = make_pdf("Bai nop", ["Hoc vien: Nguyễn Văn An", "Answer 1–4."])
    ok(b"Answer 1-4." in pdf, "PDF preserves numeric ranges")
    ok(pdf.startswith(b"%PDF-") and len(pdf) >= 12 and b"%%EOF" in pdf[-1024:], "generated PDF passes the server's content check")
    ok(is_loopback("http://localhost:19000") and is_loopback("http://127.0.0.1:18080") and not is_loopback("https://example.com")
       and not is_loopback("http://10.0.0.5:8088") and not is_loopback("http://127.0.0.1@evil.example"),
       "loopback guard accepts only local URLs")
    ok(all(ord(ch) < 128 for ch in "".join(ascii_text(n) for n in names)), "ascii_text folds Vietnamese for PDF content")
    try:
        ignored = all(subprocess.run(["git", "check-ignore", "-q", name], cwd=ROOT).returncode == 0
                      for name in (".env.demo", ".env.demo-seed.json"))
        ok(ignored, ".env.demo and the id cache are git-ignored")
    except FileNotFoundError:
        print("skip git check-ignore (git not found)")
    generated = "\n".join(env_lines())
    ok("RESOLVE_MISSED" not in generated.upper() and f"DEMO_STACK={MARKER}" in generated and "DEMO_ACTOR_PASSWORD=" in generated,
       "generated env never enables ResolveMissed and carries the isolation marker")
    ok(all(not value.endswith("=") for value in generated.splitlines() if "=" in value and not value.startswith("#")),
       "generated env has no blank secret")
    print(f"{len(failures)} failure(s)")
    return 1 if failures else 0


# ------------------------------------------------------------------ commands

def api_port(env):
    value = env.get("API_PORT", "18080")
    if not value.isascii() or not value.isdigit() or len(value) > 5 or not 1 <= int(value) <= 65535:
        raise Stop("API_PORT must be a valid local TCP port.")
    return int(value)


def require_demo_api(env):
    require_docker()
    port = compose("port", "api", "8080", web=False, capture=True)
    address = f"127.0.0.1:{api_port(env)}"
    if port.returncode or port.stdout.strip() != address:
        raise Stop("The API port is not published by the isolated demo stack. Run the demo up command first.")
    return f"http://{address}"


def cmd_up(args):
    require_docker()
    created = ensure_env()
    print("Created .env.demo with fresh random secrets." if created else "Reusing existing .env.demo (never overwritten).")
    env = load_env()
    base = f"http://127.0.0.1:{api_port(env)}"
    code = compose("up", "-d", "--build", web=args.web).returncode
    if code:
        raise Stop("docker compose up failed.")
    deadline = time.time() + 420
    probe = Api(base)
    while time.time() < deadline:
        try:
            if probe.raw("GET", "/health").status == 200:
                print(f"Demo API healthy on {base} (loopback only).")
                return 0
        except OSError:
            pass
        time.sleep(3)
    raise Stop("API did not become healthy in time; inspect: docker compose -p english-center-demo logs api")


def cmd_down(args):
    require_docker()
    load_env()
    compose("down")  # no -v: volumes (the demo data) are preserved
    print("Stopped. Data volumes kept. Destroying them is a deliberate manual step (see README).")
    return 0


def cmd_seed(args):
    chosen = args.only.split(",") if args.only else PHASES
    unknown = [p for p in chosen if p not in PHASES]
    if unknown:
        raise Stop(f"Unknown phase(s): {', '.join(unknown)}. Valid: {', '.join(PHASES)}")
    if args.dry_run:
        today = vn_today(datetime.now(timezone.utc))
        now = datetime.now(timezone.utc)
        total = sum(len(session_plan(c, today + timedelta(days=c["start"]), today + timedelta(days=c["end"]), today, now))
                    for c in CLASSES)
        print(f"plan: {len(STAFF)} staff, {len(TEACHERS)} teachers, {N_STUDENTS} students, 1 admin, {len(CLASSES)} classes, "
              f"{sum(c['n'] for c in CLASSES)} enrollments, ~{total} sessions, {len(FAQS)} FAQs, {len(FEEDBACKS)} feedback, "
              f"{len(PLACEMENTS)} placement tests; phases: {', '.join(chosen)}")
        return 0
    env = load_env()
    seeder = Seeder(env, require_demo_api(env))
    seeder.preflight()
    for name in chosen:
        getattr(seeder, f"phase_{name}")()
    seeder.report()
    return 0


def cmd_verify(args):
    env = load_env()
    return verify(env, require_demo_api(env))


def cmd_accounts(args):
    load_env()
    print("Passwords are NOT printed. Admin: BOOTSTRAP_ADMIN_PASSWORD; every other actor: DEMO_ACTOR_PASSWORD (both in .env.demo).")
    print(f"admin  : admin@{MAIL}")
    for n, name in enumerate(STAFF, 1):
        print(f"staff  : {staff_email(n)}  {name}")
    for n, name in enumerate(TEACHERS, 1):
        print(f"teacher: {teacher_email(n)}  {name}")
    for i in range(N_STUDENTS):
        print(f"student: {student_email(i)}  {student_name(i)}" + ("  (applicant: placement test only)" if i >= N_ENROLLABLE else ""))
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    up = sub.add_parser("up", help="create .env.demo if absent and start the isolated stack (docker compose, loopback ports)")
    up.add_argument("--web", action="store_true", help="also start the frontend container")
    sub.add_parser("down", help="stop the stack, KEEP data volumes")
    seed = sub.add_parser("seed", help="idempotent upsert of the demo dataset through the API")
    seed.add_argument("--only", help=f"comma list of phases: {','.join(PHASES)}")
    seed.add_argument("--dry-run", action="store_true", help="print the plan without any network access")
    sub.add_parser("verify", help="read-only checks of the seeded data")
    sub.add_parser("accounts", help="list demo account emails (no passwords)")
    sub.add_parser("self-check", help="offline checks of the generator itself")
    args = parser.parse_args()
    handlers = dict(up=cmd_up, down=cmd_down, seed=cmd_seed, verify=cmd_verify, accounts=cmd_accounts,
                    **{"self-check": lambda a: self_check()})
    try:
        return handlers[args.command](args)
    except Stop as failure:
        print(f"STOP: {failure}", file=sys.stderr)
        return 2
    except OSError as failure:  # connection refused etc.; never echo URLs with signatures
        print(f"STOP: network error ({type(failure).__name__}); is the demo stack up?", file=sys.stderr)
        return 2


if __name__ == "__main__":
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="replace")
    sys.exit(main())
