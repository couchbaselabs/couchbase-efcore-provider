using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ContosoUniversity.Data;
using ContosoUniversity.Models;
using ContosoUniversity.Models.SchoolViewModels;

namespace ContosoUniversity.Controllers
{
    public class InstructorsController : Controller
    {
        private readonly SchoolContext _context;

        public InstructorsController(SchoolContext context)
        {
            _context = context;
        }

        // GET: Instructors
        public async Task<IActionResult> Index(int? id, int? courseID)
        {
            var viewModel = new InstructorIndexData
            {
                Instructors = await _context.Instructors.OrderBy(i => i.LastName).ToListAsync(),
            };

            foreach (var instructor in viewModel.Instructors)
            {
                instructor.CourseAssignments =
                    await _context.CourseAssignments.Where(x => x.InstructorID == instructor.ID).ToListAsync();
                instructor.OfficeAssignment =
                    await _context.OfficeAssignments.SingleOrDefaultAsync(x => x.InstructorID == instructor.ID);

                foreach (var courseAssignment in instructor.CourseAssignments)
                {
                    courseAssignment.Course = await _context.Courses.Where(x => x.CourseID == courseAssignment.CourseID)
                        .SingleOrDefaultAsync();
                    courseAssignment.Course.Department =
                        await _context.Departments.Where(x => x.InstructorID == courseAssignment.InstructorID)
                            .SingleOrDefaultAsync();
                }
            }

            if (id != null)
            {
                ViewData["InstructorID"] = id.Value;
                Instructor instructor = viewModel.Instructors.Single(i => i.ID == id.Value);
                viewModel.Courses = instructor.CourseAssignments.Select(s => s.Course);
            }

            if (courseID != null)
            {
                ViewData["CourseID"] = courseID.Value;
                var selectedCourse = viewModel.Courses.Single(x => x.CourseID == courseID);
                await _context.Entry(selectedCourse).Collection(x => x.Enrollments).LoadAsync();
                await foreach (Enrollment enrollment in selectedCourse.Enrollments.ToAsyncEnumerable())
                {
                    await _context.Entry(enrollment).Reference(x => x.Student).LoadAsync();
                    await _context.Entry(enrollment).Reference(x => x.Student).LoadAsync();
                }
                viewModel.Enrollments = await selectedCourse.Enrollments.ToAsyncEnumerable().ToListAsync();
            }

            return View(viewModel);
        }

        // GET: Instructors/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var instructor = await _context.Instructors
                .FirstOrDefaultAsync(m => m.ID == id);
            if (instructor == null)
            {
                return NotFound();
            }

            return View(instructor);
        }

        // GET: Instructors/Create
        public async Task<IActionResult> Create()
        {
            var instructor = new Instructor();
            instructor.CourseAssignments = new List<CourseAssignment>();
            await PopulateAssignedCourseData(instructor).ConfigureAwait(false);
            return View();
        }

        // POST: Instructors/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("FirstMidName,HireDate,LastName,OfficeAssignment")] Instructor instructor, string[] selectedCourses)
        {
            if (selectedCourses != null)
            {
                instructor.CourseAssignments = new List<CourseAssignment>();
                foreach (var course in selectedCourses)
                {
                    var courseToAdd = new CourseAssignment { InstructorID = instructor.ID, CourseID = int.Parse(course) };
                    instructor.CourseAssignments.Add(courseToAdd);
                }
            }
            if (ModelState.IsValid)
            {
                _context.Add(instructor);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            await PopulateAssignedCourseData(instructor).ConfigureAwait(false);
            return View(instructor);
        }

        // GET: Instructors/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            /*var instructor = await _context.Instructors
                .Include(i => i.OfficeAssignment)
                .Include(i => i.CourseAssignments)
                .ThenInclude(i => i.Course)
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.ID == id);*/

            var instructor = await _context.Instructors.AsNoTracking().FirstOrDefaultAsync(x=>x.ID == id);
            if (instructor == null)
            {
                return NotFound();
            }
            instructor.OfficeAssignment =
                await _context.OfficeAssignments.AsNoTracking().SingleOrDefaultAsync(x => x.InstructorID == instructor.ID);
            instructor.CourseAssignments =
                await _context.CourseAssignments.Where(x => x.InstructorID == instructor.ID).ToListAsync();
            foreach (var courseAssignment in instructor.CourseAssignments)
            {
                courseAssignment.Course =
                    await _context.Courses.SingleOrDefaultAsync(x => x.CourseID == courseAssignment.CourseID);
            }

            await PopulateAssignedCourseData(instructor).ConfigureAwait(false);
            return View(instructor);
        }

        public async Task<IActionResult>PopulateAssignedCourseData(Instructor instructor)
        {
            var allCourses = _context.Courses;
            var instructorCourses = new HashSet<int>(instructor.CourseAssignments.Select(c => c.CourseID));
            var viewModel = new List<AssignedCourseData>();
            await foreach (var c in allCourses.AsAsyncEnumerable())
            {
                viewModel.Add(new AssignedCourseData
                {
                    CourseID = c.CourseID,
                    Title = c.Title,
                    Assigned = instructorCourses.Contains(c.CourseID)
                });
            }

            ViewData["Courses"] = viewModel;
            return null;
        }

        // POST: Instructors/Edit/5
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for 
        // more details see http://go.microsoft.com/fwlink/?LinkId=317598.

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int? id, string[] selectedCourses)
        {
            /*var instructorToUpdate = await _context.Instructors
                .Include(i => i.OfficeAssignment)
                .Include(i => i.CourseAssignments)
                    .ThenInclude(i => i.Course)
                .FirstOrDefaultAsync(m => m.ID == id);*/

            var instructorToUpdate = await _context.Instructors.AsNoTracking().FirstOrDefaultAsync(x=>x.ID == id);
            if (instructorToUpdate == null)
            {
                return NotFound();
            }
            instructorToUpdate.OfficeAssignment =
                await _context.OfficeAssignments.AsNoTracking().SingleOrDefaultAsync(x => x.InstructorID == instructorToUpdate.ID);
            instructorToUpdate.CourseAssignments =
                await _context.CourseAssignments.Where(x => x.InstructorID == instructorToUpdate.ID).ToListAsync();
            foreach (var courseAssignment in instructorToUpdate.CourseAssignments)
            {
                courseAssignment.Course =
                    await _context.Courses.SingleOrDefaultAsync(x => x.CourseID == courseAssignment.CourseID);
            }

            if (await TryUpdateModelAsync<Instructor>(
                instructorToUpdate,
                "",
                i => i.FirstMidName, i => i.LastName, i => i.HireDate, i => i.OfficeAssignment))
            {
                if (String.IsNullOrWhiteSpace(instructorToUpdate.OfficeAssignment?.Location))
                {
                    instructorToUpdate.OfficeAssignment = null;
                }
                await UpdateInstructorCourses(selectedCourses, instructorToUpdate).ConfigureAwait(false);
                try
                {
                    var count = await _context.SaveChangesAsync().ConfigureAwait(false);
                }
                catch (DbUpdateException /* ex */)
                {
                    //Log the error (uncomment ex variable name and write a log.)
                    ModelState.AddModelError("", "Unable to save changes. " +
                        "Try again, and if the problem persists, " +
                        "see your system administrator.");
                }
                return RedirectToAction(nameof(Index));
            }
            await UpdateInstructorCourses(selectedCourses, instructorToUpdate).ConfigureAwait(false);
            await PopulateAssignedCourseData(instructorToUpdate).ConfigureAwait(false);
            return View(instructorToUpdate);
        }

        public async Task<IActionResult>UpdateInstructorCourses(string[] selectedCourses, Instructor instructorToUpdate)
        {
            if (selectedCourses == null)
            {
                instructorToUpdate.CourseAssignments = new List<CourseAssignment>();
                return null;
            }

            var selectedCoursesHS = new HashSet<string>(selectedCourses);
            var instructorCourses = new HashSet<int>
                (instructorToUpdate.CourseAssignments.Select(c => c.Course.CourseID));
    
            await foreach( var course in _context.Courses.AsAsyncEnumerable()){
                if (selectedCoursesHS.Contains(course.CourseID.ToString())) {
                    if (!instructorCourses.Contains(course.CourseID)) {
                        instructorToUpdate.CourseAssignments.Add(new CourseAssignment { InstructorID = instructorToUpdate.ID, CourseID = course.CourseID });
                        _context.Instructors.Update(instructorToUpdate);
                    }
                }
                else {
                    if (instructorCourses.Contains(course.CourseID)) {
                        CourseAssignment courseToRemove = instructorToUpdate.CourseAssignments.FirstOrDefault(i => i.CourseID == course.CourseID);
                        _context.Remove(courseToRemove);
                    }
                }
            }
            return null;
        }

        // GET: Instructors/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var instructor = await _context.Instructors
                .FirstOrDefaultAsync(m => m.ID == id);
            if (instructor == null)
            {
                return NotFound();
            }

            return View(instructor);
        }

        // POST: Instructors/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
          /*  Instructor instructor = await _context.Instructors
                .Include(i => i.CourseAssignments)
                .SingleAsync(i => i.ID == id);*/

          var instructor = await _context.Instructors.SingleOrDefaultAsync(i => i.ID == id);
          instructor.CourseAssignments =
              await _context.CourseAssignments.Where(x => x.InstructorID == instructor.ID).ToListAsync();

            var departments = await _context.Departments
                .Where(d => d.InstructorID == id)
                .ToListAsync();
            departments.ForEach(d => d.InstructorID = null);

            _context.Instructors.Remove(instructor);

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool InstructorExists(int id)
        {
            return _context.Instructors.Any(e => e.ID == id);
        }
    }
}
