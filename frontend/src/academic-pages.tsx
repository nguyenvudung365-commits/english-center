import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
import { Link, useLocation, useNavigate, useParams } from "react-router-dom";
import { ApiError, getSession, getSessionGeneration, identity } from "./api";
import {
    canManageAcademic,
    createClass,
    getClass,
    isAcademicId,
    listCourses,
    listClasses,
    listTeachers,
    validateClassDraft,
    type ClassDraft,
    type ClassQuery,
} from "./academic";
import { ErrorMessage, PageHeading } from "./ui";

export function ManagerOnly({ children }: { children: ReactNode }) {
    const user = identity(getSession());
    if (!user || !canManageAcademic(user.role))
        return (
            <>
                <PageHeading
                    title="Không có quyền truy cập"
                    subtitle="Chức năng Academic này chỉ dành cho quản trị viên và giáo vụ."
                />
                <section className="panel">
                    <p>
                        Không tải dữ liệu quản lý. Vui lòng sử dụng không gian
                        phù hợp với vai trò của bạn.
                    </p>
                    <Link className="secondary inline" to="/classes">
                        Về lớp học
                    </Link>
                </section>
            </>
        );
    return children;
}
function AcademicLinks() {
    const user = identity(getSession());
    const manager = user && canManageAcademic(user.role);
    return (
        <div
            className="academic-actions"
            role="group"
            aria-label="Chức năng Academic"
        >
            <Link className="secondary inline" to="/classes">
                Tra cứu lớp
            </Link>
            <Link className="secondary inline" to="/schedule">Lịch học</Link>
            {manager && <>
            <Link className="primary inline" to="/classes/new">
                Tạo lớp học
            </Link>
            <Link className="secondary inline" to="/courses">
                Khóa học
            </Link>
            <Link className="secondary inline" to="/teachers">
                Giáo viên
            </Link>
            </>}
        </div>
    );
}
// Cleanup ignores obsolete reads; the generation check also covers logout while JSON is parsing.
export function useRead<T>(load: () => Promise<T>) {
    const [attempt, setAttempt] = useState(0);
    const [state, setState] = useState<{
        data: T | null;
        loading: boolean;
        error: unknown;
        load: (() => Promise<T>) | null;
        attempt: number;
        generation: number;
    }>({ data: null, loading: true, error: null, load: null, attempt: -1, generation: -1 });
    useEffect(() => {
        let active = true;
        const generation = getSessionGeneration();
        const scope = { load, attempt, generation };
        setState({ data: null, loading: true, error: null, ...scope });
        void load().then(
            (data) => {
                if (active && generation === getSessionGeneration())
                    setState({ data, loading: false, error: null, ...scope });
            },
            (error) => {
                if (active && generation === getSessionGeneration())
                    setState({ data: null, loading: false, error, ...scope });
            },
        );
        return () => {
            active = false;
        };
    }, [load, attempt]);
    const current = state.load === load && state.attempt === attempt && state.generation === getSessionGeneration();
    return {
        data: current ? state.data : null,
        loading: current ? state.loading : true,
        error: current ? state.error : null,
        retry: () => setAttempt((value) => value + 1),
    };
}
export function Classes() {
    const [search, setSearch] = useState("");
    const [query, setQuery] = useState<ClassQuery>({ page: 1 });
    const load = useCallback(() => listClasses(query), [query]);
    const result = useRead(load);
    const page = result.data;
    return (
        <>
            <PageHeading
                title="Lớp học"
                subtitle="Tra cứu lớp trong phạm vi tài khoản của bạn. Trạng thái được cập nhật theo ngày tại Việt Nam."
            />
            <AcademicLinks />
            <section className="panel">
                <h2>Danh sách lớp học</h2>
                <form
                    className="academic-filters"
                    onSubmit={(event) => {
                        event.preventDefault();
                        setQuery((previous) => ({ ...previous, search: search.trim(), page: 1 }));
                    }}
                >
                    <label htmlFor="class-search">Tìm mã hoặc tên lớp
                        <input id="class-search" type="search" maxLength={200} value={search} onChange={(event) => setSearch(event.target.value)} />
                    </label>
                    <label htmlFor="class-status">Trạng thái
                        <select id="class-status" value={query.status ?? ""} onChange={(event) => setQuery((previous) => ({ ...previous, status: (event.target.value || undefined) as ClassQuery["status"], page: 1 }))}>
                            <option value="">Tất cả trạng thái</option>
                            <option value="Upcoming">Sắp khai giảng</option>
                            <option value="Ongoing">Đang học</option>
                            <option value="Finished">Đã kết thúc</option>
                        </select>
                    </label>
                    <button className="primary">Tìm lớp</button>
                </form>
                {result.loading && <p role="status">Đang tải danh sách lớp…</p>}
                <ErrorMessage error={result.error} />
                {result.error ? <button className="secondary" onClick={result.retry}>Thử tải lại lớp</button> : null}
                {page && <>
                    <p role="status" className="small muted">{page.total} lớp phù hợp · Trang {page.page} / {Math.max(1, Math.ceil(page.total / page.pageSize))}</p>
                    {page.items.length === 0 && <p>Không có lớp phù hợp. Thử đổi bộ lọc hoặc quay lại trang trước.</p>}
                    <ul className="catalog-list class-list">
                        {page.items.map((item) => <li key={item.id}>
                            <div><span className="pill">{item.code}</span> <span className="pill">{classStatus(item.status)}</span>
                                <h3><Link className="accent" to={`/classes/${item.id}`}>{item.name}</Link></h3>
                                <p>{item.courseName} · {item.teacherName}</p>
                                <p className="small">{item.startDate} — {item.endDate}</p></div>
                            <div className="academic-actions">
                                <Link className="secondary inline" to={`/classes/${item.id}`}>Chi tiết lớp</Link>
                                <Link className="secondary inline" to={`/schedule?classId=${encodeURIComponent(item.id)}`}>Xem lịch</Link>
                            </div>
                        </li>)}
                    </ul>
                    <div className="academic-actions" role="group" aria-label="Phân trang lớp học">
                        <button className="secondary" disabled={page.page <= 1} onClick={() => setQuery((previous) => ({ ...previous, page: page.page - 1 }))}>Trang trước</button>
                        <button className="secondary" disabled={page.page * page.pageSize >= page.total} onClick={() => setQuery((previous) => ({ ...previous, page: page.page + 1 }))}>Trang sau</button>
                    </div>
                </>}
            </section>
        </>
    );
}
function classStatus(status: string) {
    return ({ Upcoming: "Sắp khai giảng", Ongoing: "Đang học", Finished: "Đã kết thúc" } as Record<string, string>)[status] ?? status;
}
export function ClassDetails() {
    const { id = "" } = useParams();
    const load = useCallback(() => getClass(id), [id]);
    const result = useRead(load);
    const location = useLocation();
    const success =
        location.state?.createdId === id &&
        location.state?.generation === getSessionGeneration();
    const item = result.data;
    return (
        <>
            <PageHeading
                title="Chi tiết lớp học"
                subtitle="Dữ liệu và trạng thái do máy chủ Academic cung cấp."
            />
            <AcademicLinks />
            {success && (
                <p className="notice" role="status">
                    Đã tạo lớp thành công.{" "}
                    {result.error
                        ? "Chưa đọc được chi tiết; chỉ thử tải lại, không tạo lại lớp."
                        : result.loading
                          ? "Đang tải chi tiết từ máy chủ."
                          : "Đang hiển thị dữ liệu chi tiết từ máy chủ."}
                </p>
            )}
            {result.loading && <p role="status">Đang tải chi tiết lớp…</p>}
            <ErrorMessage error={result.error} />
            {result.error instanceof ApiError &&
                result.error.status === 404 && (
                    <p>Không tìm thấy lớp với ID này.</p>
                )}
            {result.error && isAcademicId(id) ? (
                <button className="secondary" onClick={result.retry}>
                    Thử tải lại chi tiết
                </button>
            ) : null}
            {item && (
                <section className="panel class-result">
                    <span className="pill">{item.code}</span>
                    <h2>{item.name}</h2>
                    <dl className="academic-detail">
                        <dt>ID lớp</dt>
                        <dd>{item.id}</dd>
                        <dt>Mã lớp</dt>
                        <dd>{item.code}</dd>
                        <dt>Khóa học</dt>
                        <dd>
                            {item.courseCode} · {item.courseName}
                        </dd>
                        <dt>ID khóa học</dt>
                        <dd>{item.courseId}</dd>
                        <dt>Giáo viên</dt>
                        <dd>{item.teacherName}</dd>
                        <dt>ID giáo viên</dt>
                        <dd>{item.teacherId}</dd>
                        <dt>Loại lớp</dt>
                        <dd>
                            {item.classType === "Main"
                                ? "Lớp chính (Main)"
                                : item.classType === "Supplementary"
                                  ? "Lớp bổ trợ (Supplementary)"
                                  : item.classType}
                        </dd>
                        <dt>Ngày bắt đầu</dt>
                        <dd>{item.startDate}</dd>
                        <dt>Ngày kết thúc</dt>
                        <dd>{item.endDate}</dd>
                        <dt>Sĩ số tối đa</dt>
                        <dd>{item.maxStudents}</dd>
                        <dt>Học phí</dt>
                        <dd>
                            {new Intl.NumberFormat("vi-VN", {
                                style: "currency",
                                currency: "VND",
                            }).format(item.tuitionFee)}
                        </dd>
                        <dt>Trạng thái</dt>
                         <dd>{classStatus(item.status)}</dd>
                        <dt>Ngày tạo</dt>
                        <dd>{item.createdAt}</dd>
                    </dl>
                    <div className="academic-actions">
                        <Link className="secondary inline" to={`/schedule?classId=${encodeURIComponent(item.id)}`}>Xem lịch lớp</Link>
                        <Link className="secondary inline" to={`/progress/${item.id}`}>Tiến độ lớp</Link>
                        {identity(getSession()) && canManageAcademic(identity(getSession())!.role) && <Link className="primary inline" to={`/schedule?classId=${encodeURIComponent(item.id)}&create=1`}>Tạo buổi học</Link>}
                    </div>
                </section>
            )}
        </>
    );
}
const emptyDraft: ClassDraft = {
    code: "",
    name: "",
    courseId: "",
    teacherId: "",
    classType: "Main",
    startDate: "",
    endDate: "",
    maxStudents: "",
    tuitionFee: "",
};
export function ClassCreate() {
    const courses = useRead(listCourses);
    const teachers = useRead(listTeachers);
    const [draft, setDraft] = useState<ClassDraft>(emptyDraft);
    const [touched, setTouched] = useState<Record<string, boolean>>({});
    const [error, setError] = useState<unknown>();
    const [pending, setPending] = useState(false);
    const lock = useRef(false);
    const alive = useRef(false);
    useEffect(() => {
        alive.current = true;
        return () => {
            alive.current = false;
        };
    }, []);
    const navigate = useNavigate();
    const errors = validateClassDraft(draft);
    if (
        courses.data &&
        !courses.data.some(
            (item) => item.id.toLowerCase() === draft.courseId.toLowerCase(),
        )
    )
        errors.courseId = ["Chọn khóa học trong danh mục đã tải."];
    if (
        teachers.data &&
        !teachers.data.some(
            (item) => item.id.toLowerCase() === draft.teacherId.toLowerCase(),
        )
    )
        errors.teacherId = ["Chọn giáo viên trong danh mục đã tải."];
    const ready =
        !courses.loading &&
        !teachers.loading &&
        !courses.error &&
        !teachers.error &&
        !!courses.data?.length &&
        !!teachers.data?.length;
    const invalid = Object.keys(errors).length > 0;
    const serverErrors = error instanceof ApiError ? error.errors : {};
    const fieldErrors = (field: keyof ClassDraft) => [
        ...(touched[field] ? (errors[field] ?? []) : []),
        ...Object.entries(serverErrors)
            .filter(([key]) => key.toLowerCase() === field.toLowerCase())
            .flatMap(([, messages]) => messages),
    ];
    const attributes = (field: keyof ClassDraft) => ({
        id: `class-${field}`,
        name: field,
        value: draft[field],
        disabled: pending,
        "aria-invalid": fieldErrors(field).length > 0,
        "aria-describedby": fieldErrors(field).length
            ? `error-${field}`
            : undefined,
        onBlur: () =>
            setTouched((previous) => ({ ...previous, [field]: true })),
        onChange: (
            event: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>,
        ) => {
            setDraft((previous) => ({
                ...previous,
                [field]: event.target.value,
            }));
            setError(null);
        },
    });
    const field = (
        key: keyof ClassDraft,
        title: string,
        control: ReactNode,
    ) => (
        <div className="academic-field">
            <label htmlFor={`class-${key}`}>{title}</label>
            {control}
            {fieldErrors(key).length > 0 && (
                <p id={`error-${key}`} className="field-error" role="alert">
                    {fieldErrors(key).join(" ")}
                </p>
            )}
        </div>
    );
    return (
        <>
            <PageHeading
                title="Tạo lớp học"
                subtitle="Tạo lớp từ danh mục khóa học và giáo viên thực tế. Các trường đều bắt buộc."
            />
            <AcademicLinks />
            <div className="catalog-status" aria-live="polite">
                {courses.loading && <p role="status">Đang tải khóa học…</p>}
                {teachers.loading && <p role="status">Đang tải giáo viên…</p>}
                {courses.error ? (
                    <div>
                        <p>Không tải được danh mục khóa học.</p>
                        <ErrorMessage error={courses.error} />
                        <button
                            className="secondary"
                            disabled={pending}
                            onClick={courses.retry}
                        >
                            Thử lại khóa học
                        </button>
                    </div>
                ) : null}
                {teachers.error ? (
                    <div>
                        <p>Không tải được danh mục giáo viên.</p>
                        <ErrorMessage error={teachers.error} />
                        <button
                            className="secondary"
                            disabled={pending}
                            onClick={teachers.retry}
                        >
                            Thử lại giáo viên
                        </button>
                    </div>
                ) : null}
                {courses.data?.length === 0 && (
                    <p>
                        Danh mục khóa học đang trống. Liên hệ giáo vụ để bổ
                        sung; chưa thể tạo lớp.
                    </p>
                )}
                {teachers.data?.length === 0 && (
                    <p>
                        Danh mục giáo viên đang trống. Liên hệ giáo vụ để bổ
                        sung; chưa thể tạo lớp.
                    </p>
                )}
                {(courses.data?.length === 0 ||
                    teachers.data?.length === 0) && (
                    <button
                        className="secondary"
                        disabled={
                            pending || courses.loading || teachers.loading
                        }
                        onClick={() => {
                            courses.retry();
                            teachers.retry();
                        }}
                    >
                        Tải lại danh mục
                    </button>
                )}
            </div>
            <section className="panel">
                <h2>Thông tin lớp</h2>
                <form
                    className="academic-form"
                    noValidate
                    aria-busy={pending}
                    onSubmit={async (event) => {
                        event.preventDefault();
                        if (lock.current || !ready || invalid) return;
                        lock.current = true;
                        setPending(true);
                        setError(null);
                        const generation = getSessionGeneration();
                        try {
                            const created = await createClass(draft);
                            if (
                                !alive.current ||
                                generation !== getSessionGeneration()
                            )
                                return;
                            navigate(`/classes/${created.id}`, {
                                state: { createdId: created.id, generation },
                            });
                        } catch (failure) {
                            if (
                                alive.current &&
                                generation === getSessionGeneration()
                            )
                                setError(failure);
                        } finally {
                            if (
                                alive.current &&
                                generation === getSessionGeneration()
                            ) {
                                lock.current = false;
                                setPending(false);
                            }
                        }
                    }}
                >
                    <div className="academic-fields">
                        {field(
                            "code",
                            "Mã lớp",
                            <input
                                {...attributes("code")}
                                required
                                maxLength={50}
                                placeholder="IELTS-01"
                            />,
                        )}
                        {field(
                            "name",
                            "Tên lớp",
                            <input
                                {...attributes("name")}
                                required
                                maxLength={200}
                            />,
                        )}
                        {field(
                            "courseId",
                            "Khóa học",
                            <select
                                {...attributes("courseId")}
                                required
                                disabled={
                                    pending ||
                                    !courses.data?.length ||
                                    courses.loading ||
                                    !!courses.error
                                }
                            >
                                <option value="">Chọn khóa học</option>
                                {courses.data?.map((item) => (
                                    <option key={item.id} value={item.id}>
                                        {item.code} · {item.name}
                                    </option>
                                ))}
                            </select>,
                        )}
                        {field(
                            "teacherId",
                            "Giáo viên",
                            <select
                                {...attributes("teacherId")}
                                required
                                disabled={
                                    pending ||
                                    !teachers.data?.length ||
                                    teachers.loading ||
                                    !!teachers.error
                                }
                            >
                                <option value="">Chọn giáo viên</option>
                                {teachers.data?.map((item) => (
                                    <option key={item.id} value={item.id}>
                                        {item.fullName} · {item.email}
                                    </option>
                                ))}
                            </select>,
                        )}
                        {field(
                            "classType",
                            "Loại lớp",
                            <select {...attributes("classType")} required>
                                <option value="Main">Lớp chính</option>
                                <option value="Supplementary">
                                    Lớp bổ trợ
                                </option>
                            </select>,
                        )}
                        {field(
                            "maxStudents",
                            "Sĩ số tối đa",
                            <input
                                {...attributes("maxStudents")}
                                required
                                type="number"
                                min="1"
                                max="2147483647"
                                step="1"
                            />,
                        )}
                        {field(
                            "startDate",
                            "Ngày bắt đầu",
                            <input
                                {...attributes("startDate")}
                                required
                                type="date"
                                min="0001-01-01"
                                max="9999-12-31"
                            />,
                        )}
                        {field(
                            "endDate",
                            "Ngày kết thúc",
                            <input
                                {...attributes("endDate")}
                                required
                                type="date"
                                min="0001-01-01"
                                max="9999-12-31"
                            />,
                        )}
                        {field(
                            "tuitionFee",
                            "Học phí (VND)",
                            <input
                                {...attributes("tuitionFee")}
                                required
                                type="number"
                                min="0"
                                max="999999999999"
                                step="1"
                            />,
                        )}
                    </div>
                    <ErrorMessage error={error} />
                    <p id="create-help" className="small muted">
                        {pending
                            ? "Đang gửi yêu cầu. Không gửi lại hoặc rời trang trước khi có kết quả."
                            : !ready
                              ? "Chỉ có thể tạo lớp khi cả hai danh mục đã tải thành công và không trống."
                              : invalid
                                ? "Điền đầy đủ thông tin hợp lệ. Mã lớp: A–Z, 0–9, . _ -; ngày kết thúc sau ngày bắt đầu; sĩ số nguyên dương; học phí nguyên VND (có thể bằng 0)."
                                : "Thông tin hợp lệ. Tạo lớp sẽ lưu trực tiếp trên máy chủ."}
                    </p>
                    <button
                        className="primary"
                        disabled={pending || !ready || invalid}
                        aria-describedby="create-help"
                    >
                        {pending ? "Đang tạo lớp…" : "Tạo lớp học"}
                    </button>
                </form>
            </section>
        </>
    );
}
export function Catalog({ kind }: { kind: "courses" | "teachers" }) {
    const courses = kind === "courses";
    const [load] = useState<
        () => Promise<
            import("./academic").Course[] | import("./academic").Teacher[]
        >
    >(() => (courses ? listCourses : listTeachers));
    const result = useRead(load);
    const [search, setSearch] = useState("");
    const filtered = result.data?.filter((item) =>
        ("code" in item
            ? `${item.code} ${item.name} ${item.description ?? ""}`
            : `${item.fullName} ${item.email}`
        )
            .toLocaleLowerCase("vi")
            .includes(search.trim().toLocaleLowerCase("vi")),
    );
    return (
        <>
            <PageHeading
                title={courses ? "Danh mục khóa học" : "Danh mục giáo viên"}
                subtitle="Danh mục chỉ đọc từ Academic. Tìm kiếm trên dữ liệu đã tải; chưa có API thêm, sửa hoặc xóa."
            />
            <AcademicLinks />
            <section className="panel">
                <label htmlFor="catalog-search">
                    {courses
                        ? "Tìm mã, tên hoặc mô tả khóa học"
                        : "Tìm tên hoặc email giáo viên"}
                </label>
                <input
                    id="catalog-search"
                    type="search"
                    value={search}
                    onChange={(event) => setSearch(event.target.value)}
                    disabled={result.loading || !!result.error}
                />
                {result.loading && <p role="status">Đang tải danh mục…</p>}
                <ErrorMessage error={result.error} />
                {result.error ? (
                    <button className="secondary" onClick={result.retry}>
                        Thử tải lại danh mục
                    </button>
                ) : null}
                {result.data?.length === 0 && (
                    <p role="status">Danh mục hiện chưa có dữ liệu.</p>
                )}
                {!!result.data?.length && !filtered?.length && (
                    <p role="status">Không có kết quả phù hợp bộ lọc.</p>
                )}
                {!!filtered?.length && (
                    <>
                        <p className="small muted" role="status">
                            {filtered.length} / {result.data?.length} mục
                        </p>
                        <ul className="catalog-list">
                            {filtered.map((item) => (
                                <li key={item.id}>
                                    {"code" in item ? (
                                        <>
                                            <span className="pill">
                                                {item.code}
                                            </span>
                                            <h2>{item.name}</h2>
                                            <p>
                                                {item.description ??
                                                    "Chưa có mô tả."}
                                            </p>
                                        </>
                                    ) : (
                                        <>
                                            <h2>{item.fullName}</h2>
                                            <p>{item.email}</p>
                                        </>
                                    )}
                                    <p className="id muted">ID: {item.id}</p>
                                </li>
                            ))}
                        </ul>
                    </>
                )}
            </section>
        </>
    );
}
