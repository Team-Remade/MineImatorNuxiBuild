add_rules("mode.debug", "mode.release")

add_requires("libsdl2", "glad", "rmlui", "openscenegraph", "stb", "glm")

rule("utils.bin2c_with_paths")
    set_extensions(".ttf", ".rml", ".rcss", ".png")
    add_orders("utils.bin2c_with_paths", "c++.build.modules.builder")
    on_load(function(target)
        local headerroot = path.join(target:autogendir(), "rules", "utils", "bin2c")
        if not os.isdir(headerroot) then
            os.mkdir(headerroot)
        end
        target:add("includedirs", headerroot)
    end)
    on_preparecmd_file(function(target, batchcmds, sourcefile_bin, opt)
        import("rules.utils.bin2c.utils", {alias = "bin2c_utils", rootdir = os.programdir()})

        local headerroot = path.join(target:autogendir(), "rules", "utils", "bin2c")
        local relpath = path.relative(sourcefile_bin, os.projectdir())
        local headerfile = path.join(headerroot, relpath .. ".h")
        local generated = bin2c_utils.generate_headerfile(target, batchcmds, sourcefile_bin, {
            progress = opt.progress,
            headerfile = headerfile
        })

        batchcmds:add_depfiles(sourcefile_bin)
        batchcmds:set_depmtime(os.mtime(generated))
        batchcmds:set_depcache(target:dependfile(generated))
    end)

target("MineImatorNuxiBuild")
    set_kind("binary")
    set_basename("Mine Imator Nuxi Build")
    set_languages("c++17")
    add_rules("utils.bin2c_with_paths")
    add_files("src/**.cpp")
    if is_plat("windows") then
        add_files("src/appicon.rc")
    end
    add_files("assets/**")

    after_build(function (target)
        if os.isdir("data") then
            os.cp("data", target:targetdir())
        end
    end)

    after_install(function (target)
        if os.isdir("data") then
            os.cp("data", target:installdir())
        end
    end)

    add_packages("libsdl2", "glad", "rmlui", "openscenegraph", "stb", "glm")
    if is_plat("windows") then
        add_syslinks("opengl32")
    elseif is_plat("linux") then
        add_syslinks("GL")
    elseif is_plat("macosx") then
        add_frameworks("OpenGL")
    end

--
-- If you want to known more usage about xmake, please see https://xmake.io
--
-- ## FAQ
--
-- You can enter the project directory firstly before building project.
--
--   $ cd projectdir
--
-- 1. How to build project?
--
--   $ xmake
--
-- 2. How to configure project?
--
--   $ xmake f -p [macosx|linux|iphoneos ..] -a [x86_64|i386|arm64 ..] -m [debug|release]
--
-- 3. Where is the build output directory?
--
--   The default output directory is `./build` and you can configure the output directory.
--
--   $ xmake f -o outputdir
--   $ xmake
--
-- 4. How to run and debug target after building project?
--
--   $ xmake run [targetname]
--   $ xmake run -d [targetname]
--
-- 5. How to install target to the system directory or other output directory?
--
--   $ xmake install
--   $ xmake install -o installdir
--
-- 6. Add some frequently-used compilation flags in xmake.lua
--
-- @code
--    -- add debug and release modes
--    add_rules("mode.debug", "mode.release")
--
--    -- add macro definition
--    add_defines("NDEBUG", "_GNU_SOURCE=1")
--
--    -- set warning all as error
--    set_warnings("all", "error")
--
--    -- set language: c99, c++11
--    set_languages("c99", "c++11")
--
--    -- set optimization: none, faster, fastest, smallest
--    set_optimize("fastest")
--
--    -- add include search directories
--    add_includedirs("/usr/include", "/usr/local/include")
--
--    -- add link libraries and search directories
--    add_links("tbox")
--    add_linkdirs("/usr/local/lib", "/usr/lib")
--
--    -- add system link libraries
--    add_syslinks("z", "pthread")
--
--    -- add compilation and link flags
--    add_cxflags("-stdnolib", "-fno-strict-aliasing")
--    add_ldflags("-L/usr/local/lib", "-lpthread", {force = true})
--
-- @endcode
--

