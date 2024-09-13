using System.Runtime.CompilerServices;
using ContosoUniversity.Models;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ContosoUniversity.Data
{
    public class SchoolContext : DbContext
    {
        public SchoolContext(DbContextOptions<SchoolContext> options) : base(options)
        {
        }

        public DbSet<Course> Courses { get; set; }
        public DbSet<Enrollment> Enrollments { get; set; }
        public DbSet<Student> Students { get; set; }
        public DbSet<Department> Departments { get; set; }
        public DbSet<Instructor> Instructors { get; set; }
        public DbSet<OfficeAssignment> OfficeAssignments { get; set; }
        public DbSet<CourseAssignment> CourseAssignments { get; set; }
        
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Course>().ToCouchbaseCollection("course", "course");
            modelBuilder.Entity<Enrollment>().ToCouchbaseCollection("enrollment", "enrollment");
            modelBuilder.Entity<Student>().ToCouchbaseCollection("person", "person");
            modelBuilder.Entity<Instructor>().ToCouchbaseCollection("person", "person");
            modelBuilder.Entity<Person>().ToCouchbaseCollection("person", "person");
            modelBuilder.Entity<Department>().ToCouchbaseCollection("department", "department");
            modelBuilder.Entity<OfficeAssignment>().ToCouchbaseCollection("officeAssignment","officeAssignment");
            modelBuilder.Entity<CourseAssignment>().ToCouchbaseCollection("courseAssignment","courseAssignment");
            modelBuilder.Entity<CourseAssignment>()
                .HasKey(c => new { c.CourseID, c.InstructorID });
        }
    }
}
