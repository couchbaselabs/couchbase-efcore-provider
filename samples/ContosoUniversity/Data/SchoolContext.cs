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
        
        public DbSet<Person> People { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Course>().ToCouchbaseCollection("contoso", "course", "course");
            modelBuilder.Entity<Enrollment>().ToCouchbaseCollection("contoso", "enrollment", "enrollment");
            modelBuilder.Entity<Student>().ToCouchbaseCollection("contoso", "person", "student");
            modelBuilder.Entity<Department>().ToCouchbaseCollection("contoso","department", "department");
            modelBuilder.Entity<Instructor>().ToCouchbaseCollection("contoso", "person", "instructor");
            modelBuilder.Entity<OfficeAssignment>().ToCouchbaseCollection("contoso","officeAssignment","officeAssignment");
            modelBuilder.Entity<CourseAssignment>().ToCouchbaseCollection("contoso","courseAssignment","officeAssignment");
            modelBuilder.Entity<Person>().ToCouchbaseCollection("contoso", "person", "person");

            modelBuilder.Entity<CourseAssignment>()
                .HasKey(c => new { c.CourseID, c.InstructorID });
        }
    }
}
